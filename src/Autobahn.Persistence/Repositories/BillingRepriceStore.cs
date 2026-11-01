using Microsoft.EntityFrameworkCore;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;
using Autobahn.Persistence.Entities;
using Autobahn.Persistence.Infrastructure;
using Autobahn.Persistence.Mapping;

namespace Autobahn.Persistence.Repositories;

public sealed class BillingRepriceStore(GatewayDbContext dbContext) : IBillingRepriceStore
{
    /// <summary>Ids per <c>IN (...)</c>, well under SQLite's bound-parameter limit.</summary>
    private const int IdChunk = 500;

    public async Task<IReadOnlyList<BillingEventRecord>> ListEventsAsync(
        string modelId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.BillingEvents
            .AsNoTracking()
            .Where(e => e.ModelId == modelId && e.RecordedAt >= fromUtc && e.RecordedAt < toUtc)
            .OrderBy(e => e.RecordedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(BillingEntityMapper.ToRecord).ToList();
    }

    public Task ApplyAsync(
        string modelId,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<BillingEventCostUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        // Under the write lock the usage writer takes, so a batch landing meanwhile is neither lost
        // from the rollups nor counted twice in the rebuilt provider spend.
        return GatewayWriteTransaction.RunAsync(
            dbContext,
            ct => ApplyLockedAsync(modelId, from, to, updates, ct),
            cancellationToken);
    }

    private async Task ApplyLockedAsync(
        string modelId,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<BillingEventCostUpdate> updates,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rollupDeltas = new Dictionary<(DateOnly, Guid, string), decimal>();

        foreach (var chunk in updates.Chunk(IdChunk))
        {
            var ids = chunk.Select(u => u.EventId).ToList();
            var events = await dbContext.BillingEvents
                .Where(e => ids.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, cancellationToken)
                .ConfigureAwait(false);

            foreach (var update in chunk)
            {
                if (!events.TryGetValue(update.EventId, out var row))
                {
                    continue;
                }

                var key = (
                    DateOnly.FromDateTime(row.RecordedAt.UtcDateTime),
                    DailyUsageRollupEntityMapper.ToStoredTenantId(row.TenantId),
                    DailyUsageRollupEntityMapper.ToStoredCostCenter(row.CostCenter));
                rollupDeltas[key] = (rollupDeltas.TryGetValue(key, out var sum) ? sum : 0m)
                    + update.TotalCost - (row.TotalCost ?? 0m);

                row.InputCost = update.InputCost;
                row.OutputCost = update.OutputCost;
                row.TotalCost = update.TotalCost;
                row.CostSource = BillingCostSources.RateCard;
            }
        }

        // The rollups are moved by the difference rather than rebuilt: they also hold the token
        // totals, which a reprice does not touch.
        var rollups = await dbContext.DailyUsageRollups
            .Where(r => r.ModelId == modelId && r.UsageDate >= from && r.UsageDate <= to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var rollup in rollups)
        {
            if (rollupDeltas.TryGetValue((rollup.UsageDate, rollup.TenantId, rollup.CostCenter), out var delta) && delta != 0m)
            {
                rollup.TotalCost += delta;
                rollup.UpdatedAt = now;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RebuildProviderSpendAsync(modelId, from, to, now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets each of the model's provider spend buckets in the range to what the ledger now holds.
    /// A bucket with no ledger rows left is zeroed rather than deleted.
    /// </summary>
    private async Task RebuildProviderSpendAsync(
        string modelId,
        DateOnly from,
        DateOnly to,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var events = await dbContext.BillingEvents
            .AsNoTracking()
            .Where(e => e.ModelId == modelId && e.RecordedAt >= fromUtc && e.RecordedAt < toUtc && e.Provider != null)
            .Select(e => new { e.RecordedAt, e.TenantId, e.ApiKeyId, e.Provider, e.TotalCost })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var totals = events
            .Where(e => ModelProviders.IsPaid(e.Provider!))
            .GroupBy(e => (
                Date: DateOnly.FromDateTime(e.RecordedAt.UtcDateTime),
                Tenant: e.TenantId ?? Guid.Empty,
                Key: e.ApiKeyId ?? Guid.Empty,
                Provider: e.Provider!.ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => (Cost: g.Sum(e => e.TotalCost ?? 0m), Count: g.Count()));

        var buckets = await dbContext.ProviderSpendDaily
            .Where(s => s.ModelId == modelId && s.UsageDate >= from && s.UsageDate <= to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var seen = new HashSet<(DateOnly, Guid, Guid, string)>();
        foreach (var bucket in buckets)
        {
            var key = (bucket.UsageDate, bucket.TenantId, bucket.ApiKeyId, bucket.Provider.ToLowerInvariant());
            seen.Add(key);
            var (cost, count) = totals.TryGetValue(key, out var total) ? total : (0m, 0);
            if (bucket.Cost != cost || bucket.RequestCount != count)
            {
                bucket.Cost = cost;
                bucket.RequestCount = count;
                bucket.UpdatedAt = now;
            }
        }

        foreach (var (key, total) in totals)
        {
            if (seen.Contains(key))
            {
                continue;
            }

            dbContext.ProviderSpendDaily.Add(new ProviderSpendDailyEntity
            {
                Id = Guid.NewGuid(),
                UsageDate = key.Date,
                TenantId = key.Tenant,
                ApiKeyId = key.Key,
                Provider = key.Provider,
                ModelId = modelId,
                Cost = total.Cost,
                RequestCount = total.Count,
                UpdatedAt = now,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
