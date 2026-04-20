using ServantClaw.Host;

namespace ServantClaw.IntegrationTests;

public sealed class HostAssemblySmokeTests
{
    public void CanLoadHostAssemblyMarkers()
    {
        _ = typeof(Worker);
        _ = typeof(ServantClaw.Infrastructure.AssemblyMarker);
    }
}
