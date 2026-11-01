using Autobahn.Domain.RateLimiting;
using Autobahn.Policy.RateLimiting;

namespace Autobahn.Policy.Tests.RateLimiting;

/// <summary>
/// A restart used to hand every caller a full bucket. The spent buckets are exported and seated
/// again so the next process carries on from the same fill.
/// </summary>
public sealed class InMemoryDistributedRateLimitStoreSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly RateLimitPolicy Policy = new(Rpm: 60, Burst: 0, MaxConcurrentStreams: 0);

    [Fact]
    public void ExportSpentBuckets_LeavesFullBucketsOut()
    {
        var store = new InMemoryDistributedRateLimitStore();
        store.TryAcquireRequest("spent", Policy, Now);
        store.PeekRequest("never-charged", Policy, Now);

        var exported = store.ExportSpentBuckets(Now, maxBuckets: 100);

        exported.Should().ContainSingle();
        exported[0].PartitionKey.Should().Be("spent");
        exported[0].Tokens.Should().Be(59);
        exported[0].Capacity.Should().Be(60);
        exported[0].AsOfUtc.Should().Be(Now);
    }

    [Fact]
    public void ExportSpentBuckets_BucketThatRefilledSinceItsLastRequest_IsLeftOut()
    {
        var store = new InMemoryDistributedRateLimitStore();
        store.TryAcquireRequest("t", Policy, Now);

        store.ExportSpentBuckets(Now.AddSeconds(30), maxBuckets: 100).Should().BeEmpty();
    }

    [Fact]
    public void ExportSpentBuckets_HonoursTheRowLimit()
    {
        var store = new InMemoryDistributedRateLimitStore();
        for (var i = 0; i < 5; i++)
        {
            store.TryAcquireRequest("t" + i, Policy, Now);
        }

        store.ExportSpentBuckets(Now, maxBuckets: 3).Should().HaveCount(3);
    }

    [Fact]
    public void RestoreBuckets_DrainedBucket_IsStillDrainedInTheNewProcess()
    {
        var before = new InMemoryDistributedRateLimitStore();
        for (var i = 0; i < 60; i++)
        {
            before.TryAcquireRequest("t", Policy, Now).IsAcquired.Should().BeTrue();
        }

        var after = new InMemoryDistributedRateLimitStore();
        after.RestoreBuckets(before.ExportSpentBuckets(Now, 100), Now).Should().Be(1);

        after.TryAcquireRequest("t", Policy, Now).IsAcquired.Should().BeFalse();
        after.GetStats().RequestPartitions.Should().Be(1);
    }

    [Fact]
    public void RestoreBuckets_RefillsForTheTimeTheGatewayWasDown()
    {
        var before = new InMemoryDistributedRateLimitStore();
        for (var i = 0; i < 60; i++)
        {
            before.TryAcquireRequest("t", Policy, Now);
        }

        var snapshot = before.ExportSpentBuckets(Now, 100);
        var restart = Now.AddSeconds(10);
        var after = new InMemoryDistributedRateLimitStore();
        after.RestoreBuckets(snapshot, restart);

        // 60 rpm is one token a second, so ten seconds down bought ten requests and no more.
        var admitted = 0;
        for (var i = 0; i < 30; i++)
        {
            if (after.TryAcquireRequest("t", Policy, restart).IsAcquired)
            {
                admitted++;
            }
        }

        admitted.Should().Be(10);
    }

    [Fact]
    public void RestoreBuckets_PartitionAlreadyInUse_KeepsItsOwnBucket()
    {
        var store = new InMemoryDistributedRateLimitStore();
        store.TryAcquireRequest("t", Policy, Now);

        var restored = store.RestoreBuckets([new RateLimitBucketSnapshot("t", 0, 60, Now)], Now);

        restored.Should().Be(0);
        store.TryAcquireRequest("t", Policy, Now).IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void RestoreBuckets_SkipsRowsThatCannotBeUsed()
    {
        var store = new InMemoryDistributedRateLimitStore();

        var restored = store.RestoreBuckets(
            [
                new RateLimitBucketSnapshot(string.Empty, 0, 60, Now),
                new RateLimitBucketSnapshot("no-capacity", 0, 0, Now),
                new RateLimitBucketSnapshot("not-a-number", double.NaN, 60, Now),
                // Older than the store keeps an idle partition (one hour by default).
                new RateLimitBucketSnapshot("stale", 0, 60, Now.AddHours(-2)),
            ],
            Now);

        restored.Should().Be(0);
        store.GetStats().RequestPartitions.Should().Be(0);
    }

    [Fact]
    public void RestoreBuckets_SnapshotStampedInTheFuture_ResumesFromNow()
    {
        var store = new InMemoryDistributedRateLimitStore();
        store.RestoreBuckets([new RateLimitBucketSnapshot("t", 0, 60, Now.AddMinutes(5))], Now);

        // One second after the restart one token has accrued. Had the future stamp been kept,
        // refilling would have stayed paused for five minutes.
        store.TryAcquireRequest("t", Policy, Now.AddSeconds(1)).IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void RestoreBuckets_BucketInDebt_KeepsItsDebt()
    {
        var store = new InMemoryDistributedRateLimitStore();
        store.RestoreBuckets([new RateLimitBucketSnapshot("t", -5, 60, Now)], Now);

        // Five tokens owed and one needed: six seconds at one token a second.
        store.TryAcquireRequest("t", Policy, Now.AddSeconds(5)).IsAcquired.Should().BeFalse();
        store.TryAcquireRequest("t", Policy, Now.AddSeconds(6)).IsAcquired.Should().BeTrue();
    }
}
