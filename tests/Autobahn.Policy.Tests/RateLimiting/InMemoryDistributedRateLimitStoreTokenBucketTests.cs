using Microsoft.Extensions.Options;
using Autobahn.Domain.Configuration;
using Autobahn.Domain.RateLimiting;
using Autobahn.Policy.RateLimiting;

namespace Autobahn.Policy.Tests.RateLimiting;
public sealed class InMemoryDistributedRateLimitStoreTokenBucketTests
{
    private static InMemoryDistributedRateLimitStore CreateStore() => new();
    /// <summary>
    /// The bucket refills continuously, so a rejected caller is told to retry after roughly one
    /// token's worth of time — not until the next minute boundary. This is what stops SDKs (which
    /// honour Retry-After) from sleeping for most of a minute and reading it as the gateway
    /// queueing them.
    /// </summary>
    [Fact]
    public void TryAcquireRequest_WhenExhausted_RetryAfterIsOneTokenNotOneMinute()
    {
        var store = CreateStore();
        var policy = new RateLimitPolicy(Rpm: 600, Burst: 100, MaxConcurrentStreams: 0);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 1, TimeSpan.Zero);
        for (var i = 0; i < 700; i++)
        {
            store.TryAcquireRequest("t", policy, now).IsAcquired.Should().BeTrue();
        }

        var rejected = store.TryAcquireRequest("t", policy, now);
        rejected.IsAcquired.Should().BeFalse();
        rejected.RetryAfterSeconds.Should().Be(1, "600 rpm refills a token every 100 ms, so the wait rounds up to 1 s");
    }

    /// <summary>A partition that has drained its bucket earns tokens back at Rpm/60 per second.</summary>
    [Fact]
    public void TryAcquireRequest_AfterExhaustion_RefillsAtTheConfiguredRate()
    {
        var store = CreateStore();
        var policy = new RateLimitPolicy(Rpm: 60, Burst: 0, MaxConcurrentStreams: 0);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 1, TimeSpan.Zero);
        for (var i = 0; i < 60; i++)
        {
            store.TryAcquireRequest("t", policy, now).IsAcquired.Should().BeTrue();
        }

        store.TryAcquireRequest("t", policy, now).IsAcquired.Should().BeFalse();
        store.TryAcquireRequest("t", policy, now.AddMilliseconds(500)).IsAcquired.Should().BeFalse();
        // One second later exactly one token has accrued: one request passes, the next does not.
        store.TryAcquireRequest("t", policy, now.AddSeconds(1)).IsAcquired.Should().BeTrue();
        store.TryAcquireRequest("t", policy, now.AddSeconds(1)).IsAcquired.Should().BeFalse();
        // Ten more seconds: ten tokens.
        var admitted = 0;
        for (var i = 0; i < 20; i++)
        {
            if (store.TryAcquireRequest("t", policy, now.AddSeconds(11)).IsAcquired)
            {
                admitted++;
            }
        }

        admitted.Should().Be(10);
    }

    /// <summary>The bucket never overfills: a long idle period grants at most Rpm + Burst.</summary>
    [Fact]
    public void TryAcquireRequest_AfterLongIdle_CapsAtCapacity()
    {
        var store = CreateStore();
        var policy = new RateLimitPolicy(Rpm: 10, Burst: 5, MaxConcurrentStreams: 0);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 1, TimeSpan.Zero);
        store.TryAcquireRequest("t", policy, now).IsAcquired.Should().BeTrue();
        var later = now.AddHours(3);
        var admitted = 0;
        for (var i = 0; i < 40; i++)
        {
            if (store.TryAcquireRequest("t", policy, later).IsAcquired)
            {
                admitted++;
            }
        }

        admitted.Should().Be(15);
    }
}
