using Autobahn.Domain.Billing;

namespace Autobahn.Application.Abstractions;
/// <summary>Reads and rewrites the stored cost of a model's usage, for a reprice.</summary>
public interface IBillingRepriceStore
{
    /// <summary>The ledger rows of <paramref name = "modelId"/> recorded from <paramref name = "fromUtc"/> up to, not including, <paramref name = "toUtc"/>.</summary>
    Task<IReadOnlyList<BillingEventRecord>> ListEventsAsync(string modelId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
    /// <summary>
    /// In one write transaction: sets each row's new cost, moves the daily usage rollups by the
    /// difference, and rebuilds the model's <c>provider_spend_daily</c> buckets for the days
    /// <paramref name = "from"/> to <paramref name = "to"/> from the ledger.
    /// </summary>
    Task ApplyAsync(string modelId, DateOnly from, DateOnly to, IReadOnlyList<BillingEventCostUpdate> updates, CancellationToken cancellationToken = default);
}
