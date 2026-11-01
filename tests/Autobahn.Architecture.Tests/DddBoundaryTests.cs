using Autobahn.Application.Fleet;
using Autobahn.Domain.Fleet;

namespace Autobahn.Architecture.Tests;

public sealed class DddBoundaryTests
{
    [Fact]
    public void DomainHasNoApplicationInfrastructureOrFrameworkDependencies()
    {
        var references = typeof(Deployment).Assembly.GetReferencedAssemblies().Select(x => x.Name!).ToArray();
        references.Should().NotContain(x => x.StartsWith("Autobahn.", StringComparison.Ordinal) || x.StartsWith("Microsoft.", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationOnlyDependsOnDomainAmongProjectAssemblies()
    {
        typeof(FleetControlService).Assembly.GetReferencedAssemblies().Where(x => x.Name!.StartsWith("Autobahn.", StringComparison.Ordinal)).Select(x => x.Name)
            .Should().BeEquivalentTo(["Autobahn.Domain"]);
    }
}
