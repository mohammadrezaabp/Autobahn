using Microsoft.EntityFrameworkCore;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.RateLimiting;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Repositories;

public sealed class RateLimitBucketSnapshotStore(GatewayDbContext dbContext) : IRateLimitBucketSnapshotStore
{
    public async Task<IReadOnlyList<RateLimitBucketSnapshot>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.RateLimitBucketSnapshots
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities
            .Select(e => new RateLimitBucketSnapshot(e.PartitionKey, e.Tokens, e.Capacity, e.AsOfUtc))
            .ToList();
    }

    public async Task ReplaceAsync(
        IReadOnlyList<RateLimitBucketSnapshot> buckets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        // The set is small (only spent buckets have a row), so it is read whole and diffed. That
        // keeps this provider-agnostic: the EF InMemory provider used by tests does not support
        // ExecuteDelete.
        var existing = await dbContext.RateLimitBucketSnapshots
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingByKey = existing.ToDictionary(e => e.PartitionKey, StringComparer.Ordinal);

        foreach (var bucket in buckets)
        {
            if (existingByKey.Remove(bucket.PartitionKey, out var entity))
            {
                entity.Tokens = bucket.Tokens;
                entity.Capacity = bucket.Capacity;
                entity.AsOfUtc = bucket.AsOfUtc;
            }
            else
            {
                dbContext.RateLimitBucketSnapshots.Add(new RateLimitBucketSnapshotEntity
                {
                    PartitionKey = bucket.PartitionKey,
                    Tokens = bucket.Tokens,
                    Capacity = bucket.Capacity,
                    AsOfUtc = bucket.AsOfUtc,
                });
            }
        }

        // What is left was spent at the last snapshot and is full again now.
        dbContext.RateLimitBucketSnapshots.RemoveRange(existingByKey.Values);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
