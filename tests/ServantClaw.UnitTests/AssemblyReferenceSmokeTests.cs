using ServantClaw.Application;

namespace ServantClaw.UnitTests;

public sealed class AssemblyReferenceSmokeTests
{
    public void CanLoadProductionAssemblyMarkers()
    {
        _ = typeof(ServantClaw.Application.AssemblyMarker);
        _ = typeof(ServantClaw.Codex.AssemblyMarker);
        _ = typeof(ServantClaw.Domain.AssemblyMarker);
        _ = typeof(ServantClaw.Infrastructure.AssemblyMarker);
        _ = typeof(ServantClaw.Telegram.AssemblyMarker);
    }
}
