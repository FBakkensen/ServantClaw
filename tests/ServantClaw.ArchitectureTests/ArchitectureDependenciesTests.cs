using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace ServantClaw.ArchitectureTests;

public sealed class ArchitectureDependenciesTests
{
    [Fact]
    public void DomainShouldNotDependOnApplicationOrAdapters()
    {
        TestResult result = Types.InAssembly(typeof(ServantClaw.Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "ServantClaw.Application",
                "ServantClaw.Infrastructure",
                "ServantClaw.Telegram",
                "ServantClaw.Codex",
                "ServantClaw.Host")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ApplicationShouldNotDependOnInfrastructureOrTransportLayers()
    {
        TestResult result = Types.InAssembly(typeof(ServantClaw.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "ServantClaw.Infrastructure",
                "ServantClaw.Telegram",
                "ServantClaw.Codex",
                "ServantClaw.Host")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void InfrastructureAndTransportLayersShouldNotDependOnHost()
    {
        Assembly[] adapterAssemblies =
        [
            typeof(ServantClaw.Infrastructure.AssemblyMarker).Assembly,
            typeof(ServantClaw.Telegram.AssemblyMarker).Assembly,
            typeof(ServantClaw.Codex.AssemblyMarker).Assembly
        ];

        foreach (Assembly adapterAssembly in adapterAssemblies)
        {
            TestResult result = Types.InAssembly(adapterAssembly)
                .ShouldNot()
                .HaveDependencyOn("ServantClaw.Host")
                .GetResult();

            result.IsSuccessful.Should().BeTrue($"{adapterAssembly.GetName().Name} must stay reusable outside the host composition root");
        }
    }
}
