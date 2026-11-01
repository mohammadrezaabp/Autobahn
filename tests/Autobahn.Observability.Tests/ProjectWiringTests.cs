namespace Autobahn.Observability.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void Assembly_Loads()
    {
        typeof(Autobahn.Observability.Runtime.GatewayRuntimeState).Assembly.GetName().Name.Should().Be("Autobahn.Observability");
    }
}
