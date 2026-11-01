namespace Autobahn.Conformance.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void Assembly_Loads()
    {
        typeof(Autobahn.Application.Errors.GatewayErrorCode).Assembly.GetName().Name.Should().Be("Autobahn.Application");
    }
}
