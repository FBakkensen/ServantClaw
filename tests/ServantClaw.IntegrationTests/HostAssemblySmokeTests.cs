using ServantClaw.Host;
using Xunit;

namespace ServantClaw.IntegrationTests;

public sealed class HostAssemblySmokeTests
{
    [Fact]
    public void CanLoadHostAssemblyMarkers()
    {
        _ = typeof(Worker);
        _ = typeof(ServantClaw.Infrastructure.AssemblyMarker);
    }
}
