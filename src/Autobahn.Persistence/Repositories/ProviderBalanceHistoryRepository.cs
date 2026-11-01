using Microsoft.EntityFrameworkCore;
using Autobahn.Application.Abstractions;
using Autobahn.Application.Providers;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Repositories;

public sealed class ProviderBalanceHistoryRepository(GatewayDbContext dbContext) : IProviderBalanceHistoryRepository
{
    public async Task AddAsync(ProviderBalanceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        dbContext.ProviderBalanceReadings.Add(new ProviderBalanceReadingEntity
        {
            Id = Guid.NewGuid(),
            Provider = snapshot.Provider.ToLowerInvariant(),
            ReadAt = snapshot.ReadAt,
            Remaining = snapshot.Remaining,
            Currency = snapshot.Currency,
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProviderBalanceSnapshot>> ListAsync(
        string provider,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var id = provider.ToLowerInvariant();
        var baseline = await dbContext.ProviderBalanceReadings
            .AsNoTracking()
            .Where(r => r.Provider == id && r.ReadAt < fromUtc)
            .OrderByDescending(r => r.ReadAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await dbContext.ProviderBalanceReadings
            .AsNoTracking()
            .Where(r => r.Provider == id && r.ReadAt >= fromUtc && r.ReadAt <= toUtc)
            .OrderBy(r => r.ReadAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (baseline is not null)
        {
            rows.Insert(0, baseline);
        }

        return rows.Select(r => new ProviderBalanceSnapshot(r.Provider, r.ReadAt, r.Remaining, r.Currency)).ToList();
    }

    public async Task<IReadOnlyList<ProviderCharge>> ListChargesAsync(
        string provider,
        DateTimeOffset afterUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var id = provider.ToLowerInvariant();
        var rows = await dbContext.BillingEvents
            .AsNoTracking()
            .Where(e => e.Provider == id && e.RecordedAt > afterUtc && e.RecordedAt <= toUtc)
            .Select(e => new { e.RecordedAt, e.TotalCost })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => new ProviderCharge(r.RecordedAt, r.TotalCost ?? 0m)).ToList();
    }

    public Task<int> PruneAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default) =>
        dbContext.ProviderBalanceReadings
            .Where(r => r.ReadAt < beforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
}
