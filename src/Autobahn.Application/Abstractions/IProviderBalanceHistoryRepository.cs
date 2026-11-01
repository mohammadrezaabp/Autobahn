using Autobahn.Application.Providers;

namespace Autobahn.Application.Abstractions;

/// <summary>The balance readings kept for reconciliation, and the gateway's charges to set beside them.</summary>
public interface IProviderBalanceHistoryRepository
{
    Task AddAsync(ProviderBalanceSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's readings taken from <paramref name="fromUtc"/> to <paramref name="toUtc"/>,
    /// both included, preceded by the last one before <paramref name="fromUtc"/> when there is one,
    /// so the window's first stretch has a start.
    /// </summary>
    Task<IReadOnlyList<ProviderBalanceSnapshot>> ListAsync(
        string provider,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>What the gateway charged for each request it sent to the provider, recorded in (<paramref name="afterUtc"/>, <paramref name="toUtc"/>].</summary>
    Task<IReadOnlyList<ProviderCharge>> ListChargesAsync(
        string provider,
        DateTimeOffset afterUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes readings taken before <paramref name="beforeUtc"/>; returns how many.</summary>
    Task<int> PruneAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default);
}
