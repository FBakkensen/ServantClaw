using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ServantClaw.Application.Runtime;
using ServantClaw.Codex.Transport;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.Runtime;
using Xunit;

namespace ServantClaw.UnitTests.Transport;

public sealed class StdioCodexBackendClientTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly ThreadContext SampleContext =
        new(new ChatId(42), AgentKind.General, new ProjectId("demo"));

    [Fact]
    public async Task CreateThreadShouldPerformInitializeThenThreadStart()
    {
        await using TestHarness harness = await TestHarness.CreateReadyAsync();

        Task<ThreadReference> create = harness.Client.CreateThreadAsync(SampleContext, CancellationToken.None).AsTask();

        await harness.RespondToThreadStartAsync("thr_new");
        ThreadReference reference = await create.WaitAsync(TestTimeout);

        reference.Value.Should().Be("thr_new");
    }

    [Fact]
    public async Task ResumeThreadShouldSendThreadResumeAndStoreCurrentThread()
    {
        await using TestHarness harness = await TestHarness.CreateReadyAsync();

        ValueTask resume = harness.Client.ResumeThreadAsync(new ThreadReference("thr_42"), CancellationToken.None);

        using (JsonDocument doc = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            doc.RootElement.GetProperty("method").GetString().Should().Be("thread/resume");
            doc.RootElement.GetProperty("params").GetProperty("threadId").GetString().Should().Be("thr_42");
            long id = doc.RootElement.GetProperty("id").GetInt64();
            await harness.Server.WriteObjectAsync(new { id, result = new { } });
        }

        await resume;
    }

    [Fact]
    public async Task SendTurnShouldStreamFinalAnswerFromItemCompletedEvents()
    {
        await using TestHarness harness = await TestHarness.CreateReadyAsync();
        await harness.CreateThreadAsync("thr_1");

        Task<BackendTurnResult> turnTask = harness.Client
            .SendTurnAsync(new BackendTurnRequest(SampleContext, "Hello"), CancellationToken.None)
            .AsTask();

        using (JsonDocument turnStart = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            turnStart.RootElement.GetProperty("method").GetString().Should().Be("turn/start");
            turnStart.RootElement.GetProperty("params").GetProperty("threadId").GetString().Should().Be("thr_1");
            long id = turnStart.RootElement.GetProperty("id").GetInt64();
            await harness.Server.WriteObjectAsync(new { id, result = new { turn = new { id = "turn_1", status = "inProgress" } } });
        }

        await harness.Server.WriteLineAsync("""{"method":"item/completed","params":{"item":{"type":"agentMessage","phase":"final_answer","text":"Hi there."}}}""");
        await harness.Server.WriteLineAsync("""{"method":"turn/completed","params":{"turn":{"id":"turn_1","status":"completed"}}}""");

        BackendTurnResult result = await turnTask.WaitAsync(TestTimeout);
        result.RequiresApproval.Should().BeFalse();
        result.FinalResponse.Should().Be("Hi there.");
    }

    [Fact]
    public async Task SendTurnShouldThrowWhenTurnCompletesWithFailureStatus()
    {
        await using TestHarness harness = await TestHarness.CreateReadyAsync();
        await harness.CreateThreadAsync("thr_2");

        Task<BackendTurnResult> turnTask = harness.Client
            .SendTurnAsync(new BackendTurnRequest(SampleContext, "fail please"), CancellationToken.None)
            .AsTask();

        using (JsonDocument turnStart = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            long id = turnStart.RootElement.GetProperty("id").GetInt64();
            await harness.Server.WriteObjectAsync(new { id, result = new { turn = new { id = "turn_2", status = "inProgress" } } });
        }

        await harness.Server.WriteLineAsync("""{"method":"turn/completed","params":{"turn":{"id":"turn_2","status":"failed","error":{"message":"Context window exceeded","codexErrorInfo":{"type":"ContextWindowExceeded"}}}}}""");

        Func<Task> act = async () => await turnTask.WaitAsync(TestTimeout);
        BackendTurnFailedException ex = (await act.Should().ThrowAsync<BackendTurnFailedException>()).Which;
        ex.Message.Should().Be("Context window exceeded");
        ex.TurnStatus.Should().Be("failed");
        ex.CodexErrorType.Should().Be("ContextWindowExceeded");
    }

    [Fact]
    public async Task SendTurnShouldReturnApprovalRecordWhenCodexRequestsCommandApproval()
    {
        await using TestHarness harness = await TestHarness.CreateReadyAsync();
        await harness.CreateThreadAsync("thr_3");

        Task<BackendTurnResult> turnTask = harness.Client
            .SendTurnAsync(new BackendTurnRequest(SampleContext, "Run the tests"), CancellationToken.None)
            .AsTask();

        using (JsonDocument turnStart = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            long id = turnStart.RootElement.GetProperty("id").GetInt64();
            await harness.Server.WriteObjectAsync(new { id, result = new { turn = new { id = "turn_3", status = "inProgress" } } });
        }

        await harness.Server.WriteLineAsync("""{"method":"item/commandExecution/requestApproval","id":999,"params":{"itemId":"cmd_1","threadId":"thr_3","turnId":"turn_3","command":["npm","test"],"cwd":"/tmp","reason":"tests"}}""");

        BackendTurnResult result = await turnTask.WaitAsync(TestTimeout);
        result.RequiresApproval.Should().BeTrue();
        ApprovalRecord record = result.RequestedApproval!;
        record.ApprovalClass.Should().Be(ApprovalClass.StandardRiskyAction);
        record.Summary.Should().Contain("npm test").And.Contain("/tmp").And.Contain("tests");
        record.Context.ChatId.Should().Be(SampleContext.ChatId);
        record.OperationMetadata["command"].Should().Be("npm test");
        record.OperationMetadata["method"].Should().Be("item/commandExecution/requestApproval");
    }

    [Fact]
    public async Task ContinueApprovedActionShouldSendAcceptAndAggregateUntilCompletion()
    {
        await using TestHarness harness = await TestHarness.CreateReadyAsync();
        await harness.CreateThreadAsync("thr_4");

        Task<BackendTurnResult> turnTask = harness.Client
            .SendTurnAsync(new BackendTurnRequest(SampleContext, "apply changes"), CancellationToken.None)
            .AsTask();

        using (JsonDocument turnStart = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            long id = turnStart.RootElement.GetProperty("id").GetInt64();
            await harness.Server.WriteObjectAsync(new { id, result = new { turn = new { id = "turn_4", status = "inProgress" } } });
        }

        await harness.Server.WriteLineAsync("""{"method":"item/fileChange/requestApproval","id":555,"params":{"itemId":"f_1","threadId":"thr_4","turnId":"turn_4","grantRoot":"/repo","reason":"edit files"}}""");

        BackendTurnResult pending = await turnTask.WaitAsync(TestTimeout);
        pending.RequiresApproval.Should().BeTrue();

        Task<BackendTurnResult> resumeTask = harness.Client
            .ContinueApprovedActionAsync(pending.RequestedApproval!.ApprovalId, ApprovalDecision.Approved, CancellationToken.None)
            .AsTask();

        using (JsonDocument approvalResponse = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            approvalResponse.RootElement.GetProperty("id").GetInt64().Should().Be(555);
            approvalResponse.RootElement.GetProperty("result").GetString().Should().Be("accept");
        }

        await harness.Server.WriteLineAsync("""{"method":"item/completed","params":{"item":{"type":"agentMessage","phase":"final_answer","text":"Done."}}}""");
        await harness.Server.WriteLineAsync("""{"method":"turn/completed","params":{"turn":{"id":"turn_4","status":"completed"}}}""");

        BackendTurnResult resolved = await resumeTask.WaitAsync(TestTimeout);
        resolved.FinalResponse.Should().Be("Done.");
    }

    [Fact]
    public async Task ContinueApprovedActionWithDeniedShouldSendDecline()
    {
        await using TestHarness harness = await TestHarness.CreateReadyAsync();
        await harness.CreateThreadAsync("thr_5");

        Task<BackendTurnResult> turnTask = harness.Client
            .SendTurnAsync(new BackendTurnRequest(SampleContext, "ls"), CancellationToken.None)
            .AsTask();

        using (JsonDocument turnStart = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            long id = turnStart.RootElement.GetProperty("id").GetInt64();
            await harness.Server.WriteObjectAsync(new { id, result = new { turn = new { id = "turn_5", status = "inProgress" } } });
        }

        await harness.Server.WriteLineAsync("""{"method":"item/commandExecution/requestApproval","id":777,"params":{"itemId":"cmd_2","threadId":"thr_5","turnId":"turn_5","command":["rm","-rf","/"]}}""");

        BackendTurnResult pending = await turnTask.WaitAsync(TestTimeout);
        Task<BackendTurnResult> resumeTask = harness.Client
            .ContinueApprovedActionAsync(pending.RequestedApproval!.ApprovalId, ApprovalDecision.Denied, CancellationToken.None)
            .AsTask();

        using (JsonDocument deny = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            deny.RootElement.GetProperty("id").GetInt64().Should().Be(777);
            deny.RootElement.GetProperty("result").GetString().Should().Be("decline");
        }

        await harness.Server.WriteLineAsync("""{"method":"turn/completed","params":{"turn":{"id":"turn_5","status":"completed"}}}""");
        BackendTurnResult resolved = await resumeTask.WaitAsync(TestTimeout);
        resolved.FinalResponse.Should().BeNull();
    }

    [Fact]
    public async Task ContinueApprovedActionWithUnknownIdShouldThrow()
    {
        await using TestHarness harness = await TestHarness.CreateReadyAsync();

        Func<Task> act = async () =>
            await harness.Client.ContinueApprovedActionAsync(new ApprovalId("not-a-real-id"), ApprovalDecision.Approved, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendTurnWithoutPriorThreadShouldThrow()
    {
        await using TestHarness harness = await TestHarness.CreateReadyAsync();

        Func<Task> act = async () =>
            await harness.Client.SendTurnAsync(new BackendTurnRequest(SampleContext, "hi"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UnknownServerRequestShouldBeRejectedWithMethodNotFound()
    {
        await using TestHarness harness = await TestHarness.CreateReadyAsync();
        await harness.CreateThreadAsync("thr_6");

        Task<BackendTurnResult> turnTask = harness.Client
            .SendTurnAsync(new BackendTurnRequest(SampleContext, "go"), CancellationToken.None)
            .AsTask();

        using (JsonDocument turnStart = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            long id = turnStart.RootElement.GetProperty("id").GetInt64();
            await harness.Server.WriteObjectAsync(new { id, result = new { turn = new { id = "turn_6", status = "inProgress" } } });
        }

        await harness.Server.WriteLineAsync("""{"method":"something/unknown","id":321,"params":{}}""");

        using (JsonDocument reply = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            reply.RootElement.GetProperty("id").GetInt64().Should().Be(321);
            reply.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);
        }

        await harness.Server.WriteLineAsync("""{"method":"turn/completed","params":{"turn":{"id":"turn_6","status":"completed"}}}""");
        BackendTurnResult result = await turnTask.WaitAsync(TestTimeout);
        result.RequiresApproval.Should().BeFalse();
    }

    [Fact]
    public async Task GetBackendHealthShouldDelegateToProcessSupervisor()
    {
        await using TestHarness harness = await TestHarness.CreateReadyAsync();
#pragma warning disable CA2012 // NSubstitute idiom for stubbing a ValueTask-returning method.
        harness.ProcessSupervisor
            .GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BackendHealth>(new BackendHealth(true, "running")));
#pragma warning restore CA2012

        BackendHealth health = await harness.Client.GetBackendHealthAsync(CancellationToken.None);

        health.IsReady.Should().BeTrue();
        health.Detail.Should().Be("running");
    }

    [Fact]
    public async Task EnsureBackendReadyShouldCompleteOnceSessionIsPublishedAndInitialized()
    {
        await using TestHarness harness = await TestHarness.CreateAsync();
        Task ensureTask = harness.Client.EnsureBackendReadyAsync(CancellationToken.None).AsTask();

        harness.PublishSession();

        using (JsonDocument init = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            init.RootElement.GetProperty("method").GetString().Should().Be("initialize");
            long id = init.RootElement.GetProperty("id").GetInt64();
            await harness.Server.WriteObjectAsync(new { id, result = new { userAgent = "x", platformFamily = "y", platformOs = "z" } });
        }

        using (JsonDocument initialized = await harness.Server.ReadJsonLineAsync(TestTimeout))
        {
            initialized.RootElement.GetProperty("method").GetString().Should().Be("initialized");
        }

        await ensureTask.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task DisposeShouldBeIdempotent()
    {
        TestHarness harness = await TestHarness.CreateReadyAsync();

        await harness.Client.DisposeAsync();
        Func<Task> second = async () => await harness.Client.DisposeAsync();

        await second.Should().NotThrowAsync();
        await harness.DisposeAsync();
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        private TestHarness(
            FakeCodexServer server,
            BackendSessionCoordinator coordinator,
            IProcessSupervisor processSupervisor,
            StdioCodexBackendClient client,
            BackendSession session)
        {
            Server = server;
            Coordinator = coordinator;
            ProcessSupervisor = processSupervisor;
            Client = client;
            Session = session;
        }

        public FakeCodexServer Server { get; }

        public BackendSessionCoordinator Coordinator { get; }

        public IProcessSupervisor ProcessSupervisor { get; }

        public StdioCodexBackendClient Client { get; }

        public BackendSession Session { get; }

        public static async Task<TestHarness> CreateAsync()
        {
            FakeCodexServer server = new();
            BackendSessionCoordinator coordinator = new();
            IProcessSupervisor supervisor = Substitute.For<IProcessSupervisor>();
#pragma warning disable CA2012 // NSubstitute idiom for stubbing a ValueTask-returning method.
            supervisor.GetHealthAsync(Arg.Any<CancellationToken>())
                .Returns(new ValueTask<BackendHealth>(new BackendHealth(true, "running")));
#pragma warning restore CA2012
            IClock clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero));
            IIdGenerator idGenerator = Substitute.For<IIdGenerator>();
            int counter = 0;
            idGenerator.CreateApprovalId().Returns(_ => new ApprovalId($"ap-{Interlocked.Increment(ref counter)}"));

            BackendConfiguration config = new("codex", "/srv/workspace", ["app-server"]);
            StdioCodexBackendClient client = new(
                coordinator,
                supervisor,
                clock,
                idGenerator,
                config,
                NullLoggerFactory.Instance);

            BackendSession session = new(
                standardInput: server.ClientOutputStream,
                standardOutput: server.ClientInputStream,
                standardError: new MemoryStream(),
                sessionLifetime: CancellationToken.None);

            await Task.Yield();
            return new TestHarness(server, coordinator, supervisor, client, session);
        }

        public static async Task<TestHarness> CreateReadyAsync()
        {
            TestHarness harness = await CreateAsync();
            harness.PublishSession();
            await harness.PerformInitializeHandshakeAsync();
            return harness;
        }

        public void PublishSession() => Coordinator.Publish(Session);

        public async Task PerformInitializeHandshakeAsync()
        {
            Task ensureTask = Client.EnsureBackendReadyAsync(CancellationToken.None).AsTask();

            using (JsonDocument init = await Server.ReadJsonLineAsync(TestTimeout))
            {
                long id = init.RootElement.GetProperty("id").GetInt64();
                await Server.WriteObjectAsync(new { id, result = new { userAgent = "x", platformFamily = "y", platformOs = "z" } });
            }

            using JsonDocument initialized = await Server.ReadJsonLineAsync(TestTimeout);
            initialized.RootElement.GetProperty("method").GetString().Should().Be("initialized");

            await ensureTask.WaitAsync(TestTimeout);
        }

        public async Task RespondToThreadStartAsync(string threadId)
        {
            using JsonDocument doc = await Server.ReadJsonLineAsync(TestTimeout);
            doc.RootElement.GetProperty("method").GetString().Should().Be("thread/start");
            long id = doc.RootElement.GetProperty("id").GetInt64();
            await Server.WriteObjectAsync(new { id, result = new { thread = new { id = threadId } } });
        }

        public async Task CreateThreadAsync(string threadId)
        {
            Task<ThreadReference> task = Client.CreateThreadAsync(SampleContext, CancellationToken.None).AsTask();
            await RespondToThreadStartAsync(threadId);
            await task.WaitAsync(TestTimeout);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
        }
    }
}
