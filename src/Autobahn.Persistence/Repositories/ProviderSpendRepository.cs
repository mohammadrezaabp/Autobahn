using Microsoft.EntityFrameworkCore;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Persistence.Entities;
using Autobahn.Persistence.Infrastructure;

namespace Autobahn.Persistence.Repositories;
public sealed class ProviderSpendRepository(GatewayDbContext dbContext) : IProviderSpendRepository
{
    public async Task IncrementAsync(IReadOnlyList<ProviderSpendDelta> deltas, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        if (deltas.Count == 0)
        {
            return;
        }

        // Read and write under one write lock, as the daily rollups are: two writers adding to the
        // same bucket must not both start from the value before either.
        await GatewayWriteTransaction.RunAsync(dbContext, ct => ApplyAsync(deltas, ct), cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyAsync(IReadOnlyList<ProviderSpendDelta> deltas, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dates = deltas.Select(d => d.UsageDate).Distinct().ToList();
        var tenantIds = deltas.Select(d => d.TenantId ?? Guid.Empty).Distinct().ToList();
        var existing = await dbContext.ProviderSpendDaily.Where(s => dates.Contains(s.UsageDate) && tenantIds.Contains(s.TenantId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var byKey = existing.ToDictionary(KeyOf);
        foreach (var delta in deltas)
        {
            var key = (delta.UsageDate, delta.TenantId ?? Guid.Empty, delta.ApiKeyId ?? Guid.Empty, delta.Provider.ToLowerInvariant(), delta.ModelId.ToLowerInvariant());
            if (byKey.TryGetValue(key, out var entity))
            {
                entity.Cost += delta.Cost;
                entity.RequestCount += delta.RequestCount;
                entity.UpdatedAt = now;
                continue;
            }

            var created = new ProviderSpendDailyEntity
            {
                Id = Guid.NewGuid(),
                UsageDate = delta.UsageDate,
                TenantId = delta.TenantId ?? Guid.Empty,
                ApiKeyId = delta.ApiKeyId ?? Guid.Empty,
                Provider = delta.Provider,
                ModelId = delta.ModelId,
                Cost = delta.Cost,
                RequestCount = delta.RequestCount,
                UpdatedAt = now,
            };
            dbContext.ProviderSpendDaily.Add(created);
            byKey[key] = created;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProviderSpendRecord>> ListAsync(DateOnly from, DateOnly to, string? provider = null, Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var query = dbContext.ProviderSpendDaily.AsNoTracking().Where(s => s.UsageDate >= from && s.UsageDate <= to);
        if (!string.IsNullOrWhiteSpace(provider))
        {
            query = query.Where(s => s.Provider == provider);
        }

        if (tenantId is { } tenant)
        {
            query = query.Where(s => s.TenantId == tenant);
        }

        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(s => new ProviderSpendRecord(s.UsageDate, s.TenantId == Guid.Empty ? null : s.TenantId, s.ApiKeyId == Guid.Empty ? null : s.ApiKeyId, s.Provider, s.ModelId, s.Cost, s.RequestCount)).ToList();
    }

    private static (DateOnly, Guid, Guid, string, string) KeyOf(ProviderSpendDailyEntity s) => (s.UsageDate, s.TenantId, s.ApiKeyId, s.Provider.ToLowerInvariant(), s.ModelId.ToLowerInvariant());
}
