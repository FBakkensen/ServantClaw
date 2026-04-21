using ServantClaw.Application.Intake.Models;

namespace ServantClaw.Application.Intake;

public interface IChatUpdateIntake
{
    ValueTask HandleAsync(InboundChatUpdate update, CancellationToken cancellationToken);
}
