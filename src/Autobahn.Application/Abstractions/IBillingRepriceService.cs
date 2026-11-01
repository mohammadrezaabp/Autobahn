using Autobahn.Domain.Billing;

namespace Autobahn.Application.Abstractions;
/// <summary>Prices a model's stored usage again at its current rate card. Registered only where the ledger is persisted.</summary>
public interface IBillingRepriceService
{
    /// <exception cref = "BillingRepriceException">The request cannot be served as asked.</exception>
    Task<BillingRepriceReport> RepriceAsync(BillingRepriceRequest request, CancellationToken cancellationToken);
}
