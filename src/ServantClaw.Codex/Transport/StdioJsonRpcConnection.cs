using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServantClaw.Domain.Runtime;

namespace ServantClaw.Codex.Transport;

public sealed partial class StdioJsonRpcConnection : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly Stream input;
    private readonly Stream output;
    private readonly ILogger<StdioJsonRpcConnection> logger;

    private readonly Channel<IncomingMessage> incoming =
        Channel.CreateUnbounded<IncomingMessage>(new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pendingRequests = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeSource;
    private readonly Task readerTask;

    private long nextRequestId;
    private int disposed;

    public StdioJsonRpcConnection(
        Stream input,
        Stream output,
        CancellationToken sessionLifetime,
        ILogger<StdioJsonRpcConnection>? logger = null)
    {
        this.input = input ?? throw new ArgumentNullException(nameof(input));
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        this.logger = logger ?? NullLogger<StdioJsonRpcConnection>.Instance;
        lifetimeSource = CancellationTokenSource.CreateLinkedTokenSource(sessionLifetime);

        readerTask = Task.Run(() => ReadLoopAsync(lifetimeSource.Token));
    }

    public ChannelReader<IncomingMessage> IncomingMessages => incoming.Reader;

    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ThrowIfDisposed();

        long id = Interlocked.Increment(ref nextRequestId);
        TaskCompletionSource<JsonElement> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRequests[id] = tcs;

        try
        {
            await WriteMessageAsync(writer =>
            {
                writer.WriteNumber("id", id);
                writer.WriteString("method", method);
                WriteParams(writer, parameters);
            }, cancellationToken).ConfigureAwait(false);

            return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            pendingRequests.TryRemove(id, out _);
        }
    }

    public ValueTask SendResponseAsync(
        long requestId,
        object? result,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        return WriteMessageAsync(writer =>
        {
            writer.WriteNumber("id", requestId);
            writer.WritePropertyName("result");
            if (result is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                JsonSerializer.Serialize(writer, result, result.GetType(), CodexJsonSerialization.Options);
            }
        }, cancellationToken);
    }

    public ValueTask SendErrorResponseAsync(
        long requestId,
        int code,
        string message,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ThrowIfDisposed();

        return WriteMessageAsync(writer =>
        {
            writer.WriteNumber("id", requestId);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
        }, cancellationToken);
    }

    public ValueTask SendNotificationAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ThrowIfDisposed();

        return WriteMessageAsync(writer =>
        {
            writer.WriteString("method", method);
            WriteParams(writer, parameters);
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await lifetimeSource.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await readerTask.ConfigureAwait(false);
        }
        catch
        {
        }

        FailPendingRequests(new BackendUnavailableException("The backend connection was disposed."));
        incoming.Writer.TryComplete();
        writeGate.Dispose();
        lifetimeSource.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using StreamReader reader = new(input, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.ReadFailed(logger, ex);
                    break;
                }

                if (line is null)
                {
                    // EOF
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    DispatchLine(line);
                }
                catch (JsonException ex)
                {
                    Log.ParseFailed(logger, line, ex);
                }
            }
        }
        finally
        {
            FailPendingRequests(new BackendUnavailableException("The backend connection has closed."));
            incoming.Writer.TryComplete();
        }
    }

    private void DispatchLine(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;

        bool hasId = root.TryGetProperty("id", out JsonElement idElement);
        bool hasMethod = root.TryGetProperty("method", out JsonElement methodElement);
        bool hasResult = root.TryGetProperty("result", out JsonElement resultElement);
        bool hasError = root.TryGetProperty("error", out JsonElement errorElement);

        if (hasId && (hasResult || hasError))
        {
            long id = idElement.GetInt64();
            if (pendingRequests.TryRemove(id, out TaskCompletionSource<JsonElement>? tcs))
            {
                if (hasError)
                {
                    int code = errorElement.TryGetProperty("code", out JsonElement codeElement) && codeElement.ValueKind == JsonValueKind.Number
                        ? codeElement.GetInt32()
                        : 0;
                    string message = errorElement.TryGetProperty("message", out JsonElement messageElement) && messageElement.ValueKind == JsonValueKind.String
                        ? messageElement.GetString() ?? string.Empty
                        : string.Empty;
                    tcs.TrySetException(new BackendProtocolException(message, code));
                }
                else
                {
                    tcs.TrySetResult(resultElement.Clone());
                }
            }
            else
            {
                Log.OrphanResponse(logger, id);
            }
            return;
        }

        if (hasId && hasMethod)
        {
            long id = idElement.GetInt64();
            string method = methodElement.GetString() ?? string.Empty;
            JsonElement @params = root.TryGetProperty("params", out JsonElement paramsElement)
                ? paramsElement.Clone()
                : default;
            incoming.Writer.TryWrite(new IncomingServerRequest(id, method, @params));
            return;
        }

        if (hasMethod)
        {
            string method = methodElement.GetString() ?? string.Empty;
            JsonElement @params = root.TryGetProperty("params", out JsonElement paramsElement)
                ? paramsElement.Clone()
                : default;
            incoming.Writer.TryWrite(new IncomingNotification(method, @params));
            return;
        }

        Log.UnknownEnvelope(logger, line);
    }

    private async ValueTask WriteMessageAsync(Action<Utf8JsonWriter> writeBody, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { SkipValidation = false }))
        {
            writer.WriteStartObject();
            writeBody(writer);
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        ReadOnlyMemory<byte> payload = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);

        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private static void WriteParams(Utf8JsonWriter writer, object? parameters)
    {
        if (parameters is null)
        {
            return;
        }

        writer.WritePropertyName("params");
        JsonSerializer.Serialize(writer, parameters, parameters.GetType(), CodexJsonSerialization.Options);
    }

    private void FailPendingRequests(Exception exception)
    {
        foreach (KeyValuePair<long, TaskCompletionSource<JsonElement>> entry in pendingRequests.ToArray())
        {
            if (pendingRequests.TryRemove(entry.Key, out TaskCompletionSource<JsonElement>? tcs))
            {
                tcs.TrySetException(exception);
            }
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    private static partial class Log
    {
        [LoggerMessage(EventId = 700, Level = LogLevel.Warning, Message = "Failed to parse JSON-RPC line: {Line}")]
        public static partial void ParseFailed(ILogger logger, string line, Exception exception);

        [LoggerMessage(EventId = 701, Level = LogLevel.Warning, Message = "Received JSON-RPC response for unknown id {RequestId}")]
        public static partial void OrphanResponse(ILogger logger, long requestId);

        [LoggerMessage(EventId = 702, Level = LogLevel.Warning, Message = "Received JSON-RPC envelope with neither method nor response fields: {Line}")]
        public static partial void UnknownEnvelope(ILogger logger, string line);

        [LoggerMessage(EventId = 703, Level = LogLevel.Warning, Message = "JSON-RPC read loop failed")]
        public static partial void ReadFailed(ILogger logger, Exception exception);
    }
}
