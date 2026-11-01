namespace Autobahn.Billing.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void Assembly_Loads()
    {
        typeof(Autobahn.Billing.DependencyInjection.BillingServiceCollectionExtensions).Assembly
            .GetName().Name.Should().Be("Autobahn.Billing");
    }
}
