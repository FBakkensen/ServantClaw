using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace ServantClaw.UnitTests.Transport;

internal sealed class FakeCodexServer : IAsyncDisposable
{
    private readonly Pipe toClient = new();
    private readonly Pipe fromClient = new();
    private readonly StreamWriter writer;
    private readonly StreamReader reader;

    public FakeCodexServer()
    {
        writer = new StreamWriter(toClient.Writer.AsStream(), new UTF8Encoding(false))
        {
            NewLine = "\n",
            AutoFlush = false,
        };
        reader = new StreamReader(fromClient.Reader.AsStream(), new UTF8Encoding(false));
    }

    public Stream ClientInputStream => toClient.Reader.AsStream();

    public Stream ClientOutputStream => fromClient.Writer.AsStream();

    public async Task WriteLineAsync(string line)
    {
        await writer.WriteLineAsync(line);
        await writer.FlushAsync();
    }

    public async Task WriteObjectAsync(object envelope)
    {
        string line = JsonSerializer.Serialize(envelope, SharedOptions);
        await WriteLineAsync(line);
    }

    public async Task<JsonDocument> ReadJsonLineAsync(TimeSpan timeout)
    {
        using CancellationTokenSource cts = new(timeout);
        string? line = await reader.ReadLineAsync(cts.Token);
        line.Should().NotBeNull("backend should receive another JSON-RPC line before the test timeout");
        return JsonDocument.Parse(line!);
    }

    public void Close()
    {
        writer.Flush();
        toClient.Writer.Complete();
    }

    public async ValueTask DisposeAsync()
    {
        try { writer.Flush(); } catch { }
        try { toClient.Writer.Complete(); } catch { }
        try { fromClient.Writer.Complete(); } catch { }
        try { toClient.Reader.Complete(); } catch { }
        try { fromClient.Reader.Complete(); } catch { }
        await Task.Yield();
        writer.Dispose();
        reader.Dispose();
    }

    private static readonly JsonSerializerOptions SharedOptions = new(JsonSerializerDefaults.Web);
}
