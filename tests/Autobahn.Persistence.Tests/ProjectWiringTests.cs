namespace Autobahn.Persistence.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void Assembly_Loads()
    {
        typeof(Autobahn.Persistence.GatewayDbContext).Assembly.GetName().Name.Should().Be("Autobahn.Persistence");
    }
}
