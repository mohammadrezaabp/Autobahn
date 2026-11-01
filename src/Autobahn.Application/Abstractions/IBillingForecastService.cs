using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

public interface IBillingForecastService
{
    Task<UsageForecastResponse> GetForecastAsync(
        UsageForecastRequest request,
        CancellationToken cancellationToken = default);
}
