using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Autobahn.Billing.DependencyInjection;
using Autobahn.Domain.Configuration;

namespace Autobahn.Billing.Tests.DependencyInjection;

public sealed class BillingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGatewayBilling_BindsBillingOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:DefaultCurrency"] = "EUR",
                ["Billing:UsageRetentionDays"] = "30",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddGatewayBilling(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<BillingOptions>>().Value;

        options.DefaultCurrency.Should().Be("EUR");
        options.UsageRetentionDays.Should().Be(30);
    }
}
