namespace Autobahn.Security.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void Assembly_Loads()
    {
        typeof(Autobahn.Security.Services.ApiKeyValidator).Assembly.GetName().Name.Should().Be("Autobahn.Security");
    }
}
