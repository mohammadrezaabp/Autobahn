using Autobahn.Billing.Forecast;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;

namespace Autobahn.Billing.Tests.Forecast;

public sealed class NoOpBillingForecastServiceTests
{
    private readonly NoOpBillingForecastService _service = new();

    [Fact]
    public async Task GetForecastAsync_ReturnsZeroCosts()
    {
        var forecast = await _service.GetForecastAsync(
            new UsageForecastRequest { Scope = new UsageScope(null), TrailingDays = 14 });

        forecast.TrailingDays.Should().Be(14);
        forecast.TrailingTotalCost.Should().Be(0m);
        forecast.ProjectedMonthlyCost.Should().Be(0m);
        forecast.Currency.Should().Be("USD");
    }
}
