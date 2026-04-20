using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Common;

namespace ServantClaw.Domain.Routing;

public sealed record ThreadContext(ChatId ChatId, AgentKind Agent, ProjectId ProjectId);
