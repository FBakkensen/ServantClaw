using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Common;

namespace ServantClaw.Domain.Approvals;

public sealed record ApprovalContext(ChatId ChatId, AgentKind Agent, ProjectId ProjectId, ThreadReference ThreadReference);
