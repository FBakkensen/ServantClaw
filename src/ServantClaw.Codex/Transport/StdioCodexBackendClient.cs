using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.Runtime;

namespace ServantClaw.Codex.Transport;

public sealed partial class StdioCodexBackendClient : IBackendClient, IAsyncDisposable
{
    private const string CommandApprovalMethod = "item/commandExecution/requestApproval";
    private const string FileChangeApprovalMethod = "item/fileChange/requestApproval";
    private const string TurnCompletedMethod = "turn/completed";
    private const string ItemCompletedMethod = "item/completed";

    private static readonly string ClientVersion =
        typeof(StdioCodexBackendClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? typeof(StdioCodexBackendClient).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

    private readonly IBackendSessionSource sessionSource;
    private readonly IProcessSupervisor processSupervisor;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;
    private readonly BackendConfiguration backendConfiguration;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<StdioCodexBackendClient> logger;

    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private readonly Lock turnGate = new();

    private BackendSession? boundSession;
    private StdioJsonRpcConnection? connection;
    private ThreadReference? currentThread;
    private ActiveTurn? activeTurn;
    private int disposed;

    public StdioCodexBackendClient(
        IBackendSessionSource sessionSource,
        IProcessSupervisor processSupervisor,
        IClock clock,
        IIdGenerator idGenerator,
        BackendConfiguration backendConfiguration,
        ILoggerFactory loggerFactory)
    {
        this.sessionSource = sessionSource ?? throw new ArgumentNullException(nameof(sessionSource));
        this.processSupervisor = processSupervisor ?? throw new ArgumentNullException(nameof(processSupervisor));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        this.backendConfiguration = backendConfiguration ?? throw new ArgumentNullException(nameof(backendConfiguration));
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        logger = loggerFactory.CreateLogger<StdioCodexBackendClient>();
    }

    public async ValueTask EnsureBackendReadyAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _ = await GetOrEstablishConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ThreadReference> CreateThreadAsync(ThreadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ThrowIfDisposed();

        StdioJsonRpcConnection conn = await GetOrEstablishConnectionAsync(cancellationToken).ConfigureAwait(false);

        object? parameters = BuildThreadStartParameters();
        JsonElement result = await conn.SendRequestAsync("thread/start", parameters, cancellationToken).ConfigureAwait(false);
        string id = ReadThreadId(result);

        ThreadReference reference = new(id);
        currentThread = reference;
        return reference;
    }

    public async ValueTask ResumeThreadAsync(ThreadReference threadReference, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        StdioJsonRpcConnection conn = await GetOrEstablishConnectionAsync(cancellationToken).ConfigureAwait(false);

        var parameters = new { threadId = threadReference.Value };
        _ = await conn.SendRequestAsync("thread/resume", parameters, cancellationToken).ConfigureAwait(false);
        currentThread = threadReference;
    }

    public async ValueTask<BackendTurnResult> SendTurnAsync(BackendTurnRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        StdioJsonRpcConnection conn = await GetOrEstablishConnectionAsync(cancellationToken).ConfigureAwait(false);
        ThreadReference thread = currentThread
            ?? throw new InvalidOperationException("No Codex thread has been created or resumed for the current session.");

        ActiveTurn turn;
        lock (turnGate)
        {
            if (activeTurn is not null)
            {
                throw new InvalidOperationException("A Codex turn is already in progress. Resolve it before sending another.");
            }

            turn = new ActiveTurn(thread, request.Context);
            activeTurn = turn;
        }

        try
        {
            var parameters = new
            {
                threadId = thread.Value,
                input = new object[]
                {
                    new { type = "text", text = request.Message },
                },
            };

            JsonElement turnStartResult = await conn.SendRequestAsync("turn/start", parameters, cancellationToken).ConfigureAwait(false);
            turn.TurnId = ReadTurnId(turnStartResult);

            BackendTurnResult result = await AggregateAsync(turn, conn, cancellationToken).ConfigureAwait(false);
            if (!result.RequiresApproval)
            {
                ClearActiveTurn(turn);
            }

            return result;
        }
        catch
        {
            ClearActiveTurn(turn);
            throw;
        }
    }

    public async ValueTask<BackendTurnResult> ContinueApprovedActionAsync(
        ApprovalId approvalId,
        ApprovalDecision decision,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        StdioJsonRpcConnection conn = await GetOrEstablishConnectionAsync(cancellationToken).ConfigureAwait(false);

        ActiveTurn turn;
        long approvalRequestId;
        lock (turnGate)
        {
            if (activeTurn is null)
            {
                throw new InvalidOperationException("There is no Codex turn awaiting an approval decision.");
            }

            turn = activeTurn;
            if (!turn.PendingApprovals.Remove(approvalId, out approvalRequestId))
            {
                throw new InvalidOperationException(
                    $"Approval '{approvalId.Value}' does not match the currently blocked Codex turn.");
            }
        }

        try
        {
            string decisionValue = decision == ApprovalDecision.Approved ? "accept" : "decline";
            await conn.SendResponseAsync(approvalRequestId, decisionValue, cancellationToken).ConfigureAwait(false);

            BackendTurnResult result = await AggregateAsync(turn, conn, cancellationToken).ConfigureAwait(false);
            if (!result.RequiresApproval)
            {
                ClearActiveTurn(turn);
            }

            return result;
        }
        catch
        {
            ClearActiveTurn(turn);
            throw;
        }
    }

    public async ValueTask<BackendHealth> GetBackendHealthAsync(CancellationToken cancellationToken) =>
        await processSupervisor.GetHealthAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        StdioJsonRpcConnection? conn;
        lock (turnGate)
        {
            conn = connection;
            connection = null;
            boundSession = null;
            activeTurn = null;
            currentThread = null;
        }

        if (conn is not null)
        {
            await conn.DisposeAsync().ConfigureAwait(false);
        }

        sessionGate.Dispose();
    }

    private object? BuildThreadStartParameters()
    {
        if (string.IsNullOrWhiteSpace(backendConfiguration.WorkingDirectory))
        {
            return null;
        }

        return new { cwd = backendConfiguration.WorkingDirectory };
    }

    private static string ReadThreadId(JsonElement result)
    {
        if (!result.TryGetProperty("thread", out JsonElement threadElement)
            || !threadElement.TryGetProperty("id", out JsonElement idElement)
            || idElement.ValueKind != JsonValueKind.String)
        {
            throw new BackendProtocolException("Codex thread/start response did not include a thread.id string.");
        }

        string? id = idElement.GetString();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new BackendProtocolException("Codex thread/start response returned an empty thread.id.");
        }

        return id;
    }

    private static string ReadTurnId(JsonElement result)
    {
        if (!result.TryGetProperty("turn", out JsonElement turnElement)
            || !turnElement.TryGetProperty("id", out JsonElement idElement)
            || idElement.ValueKind != JsonValueKind.String)
        {
            throw new BackendProtocolException("Codex turn/start response did not include a turn.id string.");
        }

        string? id = idElement.GetString();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new BackendProtocolException("Codex turn/start response returned an empty turn.id.");
        }

        return id;
    }

    private async Task<StdioJsonRpcConnection> GetOrEstablishConnectionAsync(CancellationToken cancellationToken)
    {
        await sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BackendSession session = await sessionSource.WaitForSessionAsync(cancellationToken).ConfigureAwait(false);

            if (ReferenceEquals(boundSession, session) && connection is not null && !session.SessionLifetime.IsCancellationRequested)
            {
                return connection;
            }

            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                connection = null;
            }

            currentThread = null;
            ClearActiveTurn(null);

            StdioJsonRpcConnection newConnection = new(
                session.StandardOutput,
                session.StandardInput,
                session.SessionLifetime,
                loggerFactory.CreateLogger<StdioJsonRpcConnection>());

            try
            {
                await PerformInitializeAsync(newConnection, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await newConnection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            connection = newConnection;
            boundSession = session;
            return newConnection;
        }
        finally
        {
            sessionGate.Release();
        }
    }

    private async Task PerformInitializeAsync(StdioJsonRpcConnection conn, CancellationToken cancellationToken)
    {
        var clientInfo = new
        {
            clientInfo = new
            {
                name = "servantclaw",
                title = "ServantClaw Telegram Bot",
                version = ClientVersion,
            },
        };

        _ = await conn.SendRequestAsync("initialize", clientInfo, cancellationToken).ConfigureAwait(false);
        await conn.SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
        Log.InitializeCompleted(logger);
    }

    private async Task<BackendTurnResult> AggregateAsync(
        ActiveTurn turn,
        StdioJsonRpcConnection conn,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            IncomingMessage message = await conn.IncomingMessages.ReadAsync(cancellationToken).ConfigureAwait(false);

            switch (message)
            {
                case IncomingNotification notification:
                    BackendTurnResult? completed = HandleNotification(notification, turn);
                    if (completed is not null)
                    {
                        return completed;
                    }
                    break;

                case IncomingServerRequest serverRequest:
                    BackendTurnResult? approval = await HandleServerRequestAsync(serverRequest, turn, conn, cancellationToken).ConfigureAwait(false);
                    if (approval is not null)
                    {
                        return approval;
                    }
                    break;
            }
        }
    }

    private static BackendTurnResult? HandleNotification(IncomingNotification notification, ActiveTurn turn)
    {
        if (notification.Method == TurnCompletedMethod)
        {
            return HandleTurnCompleted(notification, turn);
        }

        if (notification.Method == ItemCompletedMethod)
        {
            AppendFinalAnswerIfPresent(notification, turn);
        }

        return null;
    }

    private static BackendTurnResult HandleTurnCompleted(IncomingNotification notification, ActiveTurn turn)
    {
        if (!notification.Params.TryGetProperty("turn", out JsonElement turnElement))
        {
            throw new BackendProtocolException("Codex turn/completed payload is missing the turn object.");
        }

        string status = turnElement.TryGetProperty("status", out JsonElement statusElement) && statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString() ?? "unknown"
            : "unknown";

        if (string.Equals(status, "completed", StringComparison.Ordinal))
        {
            string? final = turn.FinalAnswer.Length == 0 ? null : turn.FinalAnswer.ToString();
            return new BackendTurnResult(final, null);
        }

        string message = $"Codex turn ended with status '{status}'.";
        string? codexErrorType = null;

        if (turnElement.TryGetProperty("error", out JsonElement errorElement) && errorElement.ValueKind == JsonValueKind.Object)
        {
            if (errorElement.TryGetProperty("message", out JsonElement msgElement) && msgElement.ValueKind == JsonValueKind.String)
            {
                string? errorMessage = msgElement.GetString();
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    message = errorMessage;
                }
            }

            if (errorElement.TryGetProperty("codexErrorInfo", out JsonElement infoElement) && infoElement.ValueKind == JsonValueKind.Object
                && infoElement.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String)
            {
                codexErrorType = typeElement.GetString();
            }
        }

        throw new BackendTurnFailedException(message, status, codexErrorType);
    }

    private static void AppendFinalAnswerIfPresent(IncomingNotification notification, ActiveTurn turn)
    {
        if (!notification.Params.TryGetProperty("item", out JsonElement itemElement) || itemElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        bool isAgentMessage = itemElement.TryGetProperty("type", out JsonElement typeElement)
            && typeElement.ValueKind == JsonValueKind.String
            && string.Equals(typeElement.GetString(), "agentMessage", StringComparison.Ordinal);

        bool isFinal = itemElement.TryGetProperty("phase", out JsonElement phaseElement)
            && phaseElement.ValueKind == JsonValueKind.String
            && string.Equals(phaseElement.GetString(), "final_answer", StringComparison.Ordinal);

        if (!isAgentMessage || !isFinal)
        {
            return;
        }

        if (!itemElement.TryGetProperty("text", out JsonElement textElement) || textElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        string? text = textElement.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (turn.FinalAnswer.Length > 0)
        {
            turn.FinalAnswer.Append('\n');
        }

        turn.FinalAnswer.Append(text);
    }

    private async Task<BackendTurnResult?> HandleServerRequestAsync(
        IncomingServerRequest serverRequest,
        ActiveTurn turn,
        StdioJsonRpcConnection conn,
        CancellationToken cancellationToken)
    {
        if (serverRequest.Method == CommandApprovalMethod)
        {
            ApprovalRecord record = BuildCommandApproval(serverRequest, turn);
            RegisterPendingApproval(turn, record.ApprovalId, serverRequest.Id);
            return new BackendTurnResult(null, record);
        }

        if (serverRequest.Method == FileChangeApprovalMethod)
        {
            ApprovalRecord record = BuildFileChangeApproval(serverRequest, turn);
            RegisterPendingApproval(turn, record.ApprovalId, serverRequest.Id);
            return new BackendTurnResult(null, record);
        }

        Log.UnknownServerRequest(logger, serverRequest.Method, serverRequest.Id);
        await conn.SendErrorResponseAsync(
            serverRequest.Id,
            -32601,
            $"ServantClaw does not handle '{serverRequest.Method}'.",
            cancellationToken).ConfigureAwait(false);
        return null;
    }

    private void RegisterPendingApproval(ActiveTurn turn, ApprovalId approvalId, long requestId)
    {
        lock (turnGate)
        {
            turn.PendingApprovals[approvalId] = requestId;
        }
    }

    private ApprovalRecord BuildCommandApproval(IncomingServerRequest request, ActiveTurn turn)
    {
        string command = ReadCommandDisplay(request.Params);
        string? cwd = TryReadString(request.Params, "cwd");
        string? reason = TryReadString(request.Params, "reason");

        string summary = BuildCommandSummary(command, cwd, reason);
        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["method"] = request.Method,
            ["command"] = command,
        };
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            metadata["cwd"] = cwd;
        }
        if (!string.IsNullOrWhiteSpace(reason))
        {
            metadata["reason"] = reason;
        }

        return CreateApproval(turn, summary, metadata);
    }

    private ApprovalRecord BuildFileChangeApproval(IncomingServerRequest request, ActiveTurn turn)
    {
        string? grantRoot = TryReadString(request.Params, "grantRoot");
        string? reason = TryReadString(request.Params, "reason");

        string summary = BuildFileChangeSummary(grantRoot, reason);
        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["method"] = request.Method,
        };
        if (!string.IsNullOrWhiteSpace(grantRoot))
        {
            metadata["grantRoot"] = grantRoot;
        }
        if (!string.IsNullOrWhiteSpace(reason))
        {
            metadata["reason"] = reason;
        }

        return CreateApproval(turn, summary, metadata);
    }

    private ApprovalRecord CreateApproval(ActiveTurn turn, string summary, IReadOnlyDictionary<string, string> metadata)
    {
        ApprovalContext approvalContext = new(
            turn.ChatContext.ChatId,
            turn.ChatContext.Agent,
            turn.ChatContext.ProjectId,
            turn.Thread);

        return new ApprovalRecord(
            idGenerator.CreateApprovalId(),
            ApprovalClass.StandardRiskyAction,
            approvalContext,
            summary,
            clock.UtcNow,
            metadata);
    }

    private static string ReadCommandDisplay(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("command", out JsonElement commandElement))
        {
            return "<unknown command>";
        }

        if (commandElement.ValueKind == JsonValueKind.Array)
        {
            StringBuilder builder = new();
            bool first = true;
            foreach (JsonElement part in commandElement.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append(' ');
                }

                builder.Append(part.GetString());
                first = false;
            }

            return builder.Length == 0 ? "<unknown command>" : builder.ToString();
        }

        if (commandElement.ValueKind == JsonValueKind.String)
        {
            return commandElement.GetString() ?? "<unknown command>";
        }

        return "<unknown command>";
    }

    private static string BuildCommandSummary(string command, string? cwd, string? reason)
    {
        StringBuilder builder = new();
        builder.Append("Run: ").Append(command);
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            builder.Append(" (cwd: ").Append(cwd).Append(')');
        }
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.Append(" — ").Append(reason);
        }
        return builder.ToString();
    }

    private static string BuildFileChangeSummary(string? grantRoot, string? reason)
    {
        StringBuilder builder = new();
        builder.Append("Modify files");
        if (!string.IsNullOrWhiteSpace(grantRoot))
        {
            builder.Append(" under ").Append(grantRoot);
        }
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.Append(" — ").Append(reason);
        }
        return builder.ToString();
    }

    private static string? TryReadString(JsonElement parameters, string name)
    {
        if (!parameters.TryGetProperty(name, out JsonElement element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    private void ClearActiveTurn(ActiveTurn? expected)
    {
        lock (turnGate)
        {
            if (expected is null || ReferenceEquals(activeTurn, expected))
            {
                activeTurn = null;
            }
        }
    }

    private sealed class ActiveTurn
    {
        public ActiveTurn(ThreadReference thread, ThreadContext chatContext)
        {
            Thread = thread;
            ChatContext = chatContext;
        }

        public ThreadReference Thread { get; }

        public ThreadContext ChatContext { get; }

        public string? TurnId { get; set; }

        public StringBuilder FinalAnswer { get; } = new();

        public Dictionary<ApprovalId, long> PendingApprovals { get; } = new();
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 800, Level = LogLevel.Information, Message = "Codex initialize handshake completed")]
        public static partial void InitializeCompleted(ILogger logger);

        [LoggerMessage(EventId = 801, Level = LogLevel.Warning, Message = "Ignoring unknown Codex server request {Method} (id {RequestId})")]
        public static partial void UnknownServerRequest(ILogger logger, string method, long requestId);
    }
}
