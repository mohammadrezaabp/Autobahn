namespace Autobahn.Policy.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void Assembly_Loads()
    {
        typeof(Autobahn.Policy.RateLimiting.RateLimitPolicyResolver).Assembly.GetName().Name.Should().Be("Autobahn.Policy");
    }
}
