using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ServantClaw.Codex.Transport;
using ServantClaw.Domain.Runtime;
using Xunit;

namespace ServantClaw.UnitTests.Transport;

public sealed class StdioJsonRpcConnectionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SendRequestShouldWriteEnvelopeWithIncrementingIdAndReturnResult()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        Task<JsonElement> firstRequest = connection.SendRequestAsync("thread/start", new { cwd = "/tmp" }, CancellationToken.None);
        string firstLine = await pipes.ReadBackendLineAsync();
        using (JsonDocument doc = JsonDocument.Parse(firstLine))
        {
            doc.RootElement.GetProperty("id").GetInt64().Should().Be(1);
            doc.RootElement.GetProperty("method").GetString().Should().Be("thread/start");
            doc.RootElement.GetProperty("params").GetProperty("cwd").GetString().Should().Be("/tmp");
        }

        await pipes.SendBackendLineAsync("""{"id":1,"result":{"thread":{"id":"thr_123"}}}""");
        JsonElement firstResult = await firstRequest.WaitAsync(TestTimeout);
        firstResult.GetProperty("thread").GetProperty("id").GetString().Should().Be("thr_123");

        Task<JsonElement> secondRequest = connection.SendRequestAsync("turn/interrupt", null, CancellationToken.None);
        string secondLine = await pipes.ReadBackendLineAsync();
        using (JsonDocument doc = JsonDocument.Parse(secondLine))
        {
            doc.RootElement.GetProperty("id").GetInt64().Should().Be(2);
        }

        await pipes.SendBackendLineAsync("""{"id":2,"result":{}}""");
        await secondRequest.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task SendRequestShouldThrowProtocolExceptionWhenBackendRespondsWithError()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        Task<JsonElement> request = connection.SendRequestAsync("turn/start", new { threadId = "thr_1" }, CancellationToken.None);
        await pipes.ReadBackendLineAsync();
        await pipes.SendBackendLineAsync("""{"id":1,"error":{"code":-32001,"message":"Server overloaded; retry later."}}""");

        Func<Task> act = async () => await request.WaitAsync(TestTimeout);
        BackendProtocolException exception = (await act.Should().ThrowAsync<BackendProtocolException>()).Which;
        exception.Message.Should().Be("Server overloaded; retry later.");
        exception.ErrorCode.Should().Be(-32001);
    }

    [Fact]
    public async Task IncomingNotificationShouldBeDispatchedToReader()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        await pipes.SendBackendLineAsync("""{"method":"turn/started","params":{"turn":{"id":"turn_1"}}}""");

        IncomingMessage message = await connection.IncomingMessages.ReadAsync(CancellationTokenWithTimeout());
        IncomingNotification notification = message.Should().BeOfType<IncomingNotification>().Subject;
        notification.Method.Should().Be("turn/started");
        notification.Params.GetProperty("turn").GetProperty("id").GetString().Should().Be("turn_1");
    }

    [Fact]
    public async Task IncomingServerRequestShouldBeDispatchedToReader()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        await pipes.SendBackendLineAsync("""{"method":"item/commandExecution/requestApproval","id":100,"params":{"command":["ls","-la"],"cwd":"/tmp"}}""");

        IncomingMessage message = await connection.IncomingMessages.ReadAsync(CancellationTokenWithTimeout());
        IncomingServerRequest request = message.Should().BeOfType<IncomingServerRequest>().Subject;
        request.Id.Should().Be(100);
        request.Method.Should().Be("item/commandExecution/requestApproval");
        request.Params.GetProperty("cwd").GetString().Should().Be("/tmp");
    }

    [Fact]
    public async Task SendResponseShouldEmitEnvelopeWithMatchingId()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        await connection.SendResponseAsync(42, "accept", CancellationToken.None);
        string line = await pipes.ReadBackendLineAsync();

        using JsonDocument doc = JsonDocument.Parse(line);
        doc.RootElement.GetProperty("id").GetInt64().Should().Be(42);
        doc.RootElement.GetProperty("result").GetString().Should().Be("accept");
        doc.RootElement.TryGetProperty("method", out _).Should().BeFalse("responses carry result/id only");
    }

    [Fact]
    public async Task SendNotificationShouldEmitMethodWithoutId()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        await connection.SendNotificationAsync("initialized", new { }, CancellationToken.None);
        string line = await pipes.ReadBackendLineAsync();

        using JsonDocument doc = JsonDocument.Parse(line);
        doc.RootElement.GetProperty("method").GetString().Should().Be("initialized");
        doc.RootElement.TryGetProperty("id", out _).Should().BeFalse();
    }

    [Fact]
    public async Task BackendEofShouldFailPendingRequestsWithBackendUnavailable()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        Task<JsonElement> request = connection.SendRequestAsync("thread/start", null, CancellationToken.None);
        await pipes.ReadBackendLineAsync();

        pipes.CloseBackendOutput();

        Func<Task> act = async () => await request.WaitAsync(TestTimeout);
        await act.Should().ThrowAsync<BackendUnavailableException>();
    }

    [Fact]
    public async Task SessionLifetimeCancellationShouldFailPendingRequests()
    {
        using CancellationTokenSource sessionCts = new();
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection(sessionCts.Token);

        Task<JsonElement> request = connection.SendRequestAsync("thread/start", null, CancellationToken.None);
        await pipes.ReadBackendLineAsync();

        await sessionCts.CancelAsync();

        Func<Task> act = async () => await request.WaitAsync(TestTimeout);
        await act.Should().ThrowAsync<BackendUnavailableException>();
    }

    [Fact]
    public async Task RequestCancellationShouldObserveOperationCanceled()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        using CancellationTokenSource requestCts = new();
        Task<JsonElement> request = connection.SendRequestAsync("thread/start", null, requestCts.Token);
        await pipes.ReadBackendLineAsync();

        await requestCts.CancelAsync();

        Func<Task> act = async () => await request.WaitAsync(TestTimeout);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MalformedJsonLineShouldBeDroppedWithoutFailingReader()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        await pipes.SendBackendLineAsync("not valid json");
        await pipes.SendBackendLineAsync("""{"method":"turn/started","params":{}}""");

        IncomingMessage message = await connection.IncomingMessages.ReadAsync(CancellationTokenWithTimeout());
        message.Should().BeOfType<IncomingNotification>()
            .Which.Method.Should().Be("turn/started");
    }

    [Fact]
    public void ConstructorShouldRejectNullStreams()
    {
        Action nullInput = () => _ = new StdioJsonRpcConnection(null!, new MemoryStream(), CancellationToken.None);
        Action nullOutput = () => _ = new StdioJsonRpcConnection(new MemoryStream(), null!, CancellationToken.None);

        nullInput.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("input");
        nullOutput.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("output");
    }

    [Fact]
    public async Task SendRequestShouldRejectEmptyMethod()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        Func<Task> act = async () => await connection.SendRequestAsync(" ", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendNotificationShouldRejectEmptyMethod()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        Func<Task> act = async () => await connection.SendNotificationAsync(string.Empty, null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OperationsAfterDisposeShouldThrow()
    {
        using TestPipes pipes = new();
        StdioJsonRpcConnection connection = pipes.CreateConnection();
        await connection.DisposeAsync();

        Func<Task> send = async () => await connection.SendRequestAsync("x", null, CancellationToken.None);
        Func<Task> respond = async () => await connection.SendResponseAsync(1, null, CancellationToken.None);
        Func<Task> notify = async () => await connection.SendNotificationAsync("x", null, CancellationToken.None);

        await send.Should().ThrowAsync<ObjectDisposedException>();
        await respond.Should().ThrowAsync<ObjectDisposedException>();
        await notify.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task OrphanResponseShouldNotInterruptSubsequentRequests()
    {
        using TestPipes pipes = new();
        await using StdioJsonRpcConnection connection = pipes.CreateConnection();

        // Backend emits a spurious response for an id the client never used.
        await pipes.SendBackendLineAsync("""{"id":999,"result":{"ignored":true}}""");

        Task<JsonElement> request = connection.SendRequestAsync("thread/start", null, CancellationToken.None);
        await pipes.ReadBackendLineAsync();
        await pipes.SendBackendLineAsync("""{"id":1,"result":{"thread":{"id":"thr_abc"}}}""");

        JsonElement result = await request.WaitAsync(TestTimeout);
        result.GetProperty("thread").GetProperty("id").GetString().Should().Be("thr_abc");
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        using TestPipes pipes = new();
        StdioJsonRpcConnection connection = pipes.CreateConnection();

        await connection.DisposeAsync();
        Func<Task> second = async () => await connection.DisposeAsync();
        await second.Should().NotThrowAsync();
    }

    private static CancellationToken CancellationTokenWithTimeout() =>
        new CancellationTokenSource(TestTimeout).Token;

    private sealed class TestPipes : IDisposable
    {
        private readonly Pipe toConnection = new();
        private readonly Pipe fromConnection = new();
        private readonly StreamWriter backendWriter;
        private readonly StreamReader backendReader;

        public TestPipes()
        {
            backendWriter = new StreamWriter(toConnection.Writer.AsStream(), new UTF8Encoding(false))
            {
                NewLine = "\n",
                AutoFlush = false,
            };
            backendReader = new StreamReader(fromConnection.Reader.AsStream(), new UTF8Encoding(false));
        }

        public StdioJsonRpcConnection CreateConnection(CancellationToken sessionLifetime = default) =>
            new(toConnection.Reader.AsStream(), fromConnection.Writer.AsStream(), sessionLifetime);

        public async Task SendBackendLineAsync(string line)
        {
            await backendWriter.WriteLineAsync(line);
            await backendWriter.FlushAsync();
        }

        public async Task<string> ReadBackendLineAsync()
        {
            using CancellationTokenSource cts = new(TestTimeout);
            string? line = await backendReader.ReadLineAsync(cts.Token);
            line.Should().NotBeNull();
            return line!;
        }

        public void CloseBackendOutput()
        {
            backendWriter.Flush();
            toConnection.Writer.Complete();
        }

        public void Dispose()
        {
            try { toConnection.Writer.Complete(); } catch { }
            try { fromConnection.Writer.Complete(); } catch { }
            try { toConnection.Reader.Complete(); } catch { }
            try { fromConnection.Reader.Complete(); } catch { }
            backendWriter.Dispose();
            backendReader.Dispose();
        }
    }
}
