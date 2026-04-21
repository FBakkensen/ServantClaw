using System.Text.Json;

namespace ServantClaw.Codex.Transport;

public abstract record IncomingMessage;

public sealed record IncomingNotification(string Method, JsonElement Params) : IncomingMessage;

public sealed record IncomingServerRequest(long Id, string Method, JsonElement Params) : IncomingMessage;
