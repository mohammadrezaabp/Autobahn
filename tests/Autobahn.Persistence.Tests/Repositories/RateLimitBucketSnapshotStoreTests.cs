using Autobahn.Domain.RateLimiting;
using Autobahn.Persistence.Repositories;
using Autobahn.Persistence.Tests.Infrastructure;

namespace Autobahn.Persistence.Tests.Repositories;

public sealed class RateLimitBucketSnapshotStoreTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadAsync_WhenEmpty_ReturnsEmpty()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(RateLimitBucketSnapshotStoreTests) + nameof(LoadAsync_WhenEmpty_ReturnsEmpty));

        (await new RateLimitBucketSnapshotStore(db).LoadAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ReplaceThenLoad_RoundTripsBuckets()
    {
        var dbName = nameof(RateLimitBucketSnapshotStoreTests) + nameof(ReplaceThenLoad_RoundTripsBuckets);

        await using (var db = PersistenceTestDbContextFactory.CreateInMemory(dbName))
        {
            await new RateLimitBucketSnapshotStore(db).ReplaceAsync([
                new RateLimitBucketSnapshot("tenant-a", 12.5, 315, AsOf),
                new RateLimitBucketSnapshot("auth_failure:10.0.0.0", -3, 80, AsOf),
            ]);
        }

        await using (var db = PersistenceTestDbContextFactory.CreateInMemory(dbName))
        {
            var loaded = await new RateLimitBucketSnapshotStore(db).LoadAsync();

            loaded.Should().BeEquivalentTo([
                new RateLimitBucketSnapshot("tenant-a", 12.5, 315, AsOf),
                new RateLimitBucketSnapshot("auth_failure:10.0.0.0", -3, 80, AsOf),
            ]);
        }
    }

    [Fact]
    public async Task ReplaceAsync_UpdatesKeptRowsAndRemovesTheRest()
    {
        var dbName = nameof(RateLimitBucketSnapshotStoreTests) + nameof(ReplaceAsync_UpdatesKeptRowsAndRemovesTheRest);

        await using (var db = PersistenceTestDbContextFactory.CreateInMemory(dbName))
        {
            await new RateLimitBucketSnapshotStore(db).ReplaceAsync([
                new RateLimitBucketSnapshot("kept", 1, 60, AsOf),
                new RateLimitBucketSnapshot("refilled", 2, 60, AsOf),
            ]);
        }

        await using (var db = PersistenceTestDbContextFactory.CreateInMemory(dbName))
        {
            await new RateLimitBucketSnapshotStore(db).ReplaceAsync([
                new RateLimitBucketSnapshot("kept", 40, 60, AsOf.AddSeconds(10)),
            ]);
        }

        await using (var db = PersistenceTestDbContextFactory.CreateInMemory(dbName))
        {
            var loaded = await new RateLimitBucketSnapshotStore(db).LoadAsync();

            loaded.Should().ContainSingle();
            loaded[0].Should().Be(new RateLimitBucketSnapshot("kept", 40, 60, AsOf.AddSeconds(10)));
        }
    }

    [Fact]
    public async Task ReplaceAsync_WithNothingSpent_EmptiesTheTable()
    {
        var dbName = nameof(RateLimitBucketSnapshotStoreTests) + nameof(ReplaceAsync_WithNothingSpent_EmptiesTheTable);

        await using (var db = PersistenceTestDbContextFactory.CreateInMemory(dbName))
        {
            await new RateLimitBucketSnapshotStore(db).ReplaceAsync([new RateLimitBucketSnapshot("t", 1, 60, AsOf)]);
        }

        await using (var db = PersistenceTestDbContextFactory.CreateInMemory(dbName))
        {
            await new RateLimitBucketSnapshotStore(db).ReplaceAsync([]);
        }

        await using (var db = PersistenceTestDbContextFactory.CreateInMemory(dbName))
        {
            (await new RateLimitBucketSnapshotStore(db).LoadAsync()).Should().BeEmpty();
        }
    }
}
