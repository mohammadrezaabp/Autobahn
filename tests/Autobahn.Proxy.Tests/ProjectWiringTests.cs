namespace Autobahn.Proxy.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void Assembly_Loads()
    {
        typeof(Autobahn.Proxy.Middleware.ModelRouterMiddleware).Assembly.GetName().Name.Should().Be("Autobahn.Proxy");
    }
}
