using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;

namespace Autobahn.Billing.Forecast;

public sealed class NoOpBillingForecastService : IBillingForecastService
{
    public Task<UsageForecastResponse> GetForecastAsync(
        UsageForecastRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new UsageForecastResponse
        {
            TrailingDays = request.TrailingDays,
            TrailingTotalCost = 0m,
            ProjectedMonthlyCost = 0m,
            Currency = "USD",
        });
}
