using Autobahn.Domain.RateLimiting;
using Autobahn.Policy.RateLimiting;

namespace Autobahn.Policy.Tests.RateLimiting;

/// <summary>
/// The tokens-per-minute control: a reservation on admission, and the settlement that replaces it
/// with what the request really used.
/// </summary>
public sealed class InMemoryDistributedRateLimitStoreTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryReserveTokens_WithinTheBudget_AdmitsAndReportsWhatIsLeft()
    {
        var store = new InMemoryDistributedRateLimitStore();

        var result = store.TryReserveTokens(Rules(("t:acme", 1000)), 400, Now);

        result.IsAcquired.Should().BeTrue();
        result.Scope.Should().Be(RateLimitScope.Tenant);
        result.Limit.Should().Be(1000);
        result.Remaining.Should().Be(600);
    }

    /// <summary>
    /// Retry-After is the time the bucket needs to hold this request, not the time to its next
    /// single token: a client told to come back in a second would be refused again.
    /// </summary>
    [Fact]
    public void TryReserveTokens_OverTheBudget_RefusesAndSaysWhenTheRequestWillFit()
    {
        var store = new InMemoryDistributedRateLimitStore();
        var rules = Rules(("t:acme", 600));
        store.TryReserveTokens(rules, 500, Now).IsAcquired.Should().BeTrue();

        var refused = store.TryReserveTokens(rules, 300, Now);

        refused.IsAcquired.Should().BeFalse();
        refused.RejectionReason.Should().Be(GatewayRateLimitReason.TokenLimitExceeded);
        refused.Control.Should().Be(RateLimitControl.Tokens);
        // 100 left, 300 wanted, 10 tokens a second.
        refused.RetryAfterSeconds.Should().Be(20);
        refused.ConfiguredRpm.Should().Be(0, "a token reading must not put tokens into a per-minute request column");
    }

    [Fact]
    public void TryReserveTokens_RefillsAtTheConfiguredRate()
    {
        var store = new InMemoryDistributedRateLimitStore();
        var rules = Rules(("t:acme", 600));
        store.TryReserveTokens(rules, 600, Now).IsAcquired.Should().BeTrue();

        store.TryReserveTokens(rules, 300, Now.AddSeconds(29)).IsAcquired.Should().BeFalse();
        store.TryReserveTokens(rules, 300, Now.AddSeconds(30)).IsAcquired.Should().BeTrue();
    }

    /// <summary>
    /// A caller refused by its own token limit must not keep tokens in the model's shared budget.
    /// </summary>
    [Fact]
    public void TryReserveTokens_WhenALaterRuleRefuses_HandsBackTheEarlierOnes()
    {
        var store = new InMemoryDistributedRateLimitStore();
        var model = Rules(("m:gpt-4", 1000));
        var both = Rules(("m:gpt-4", 1000), ("t:acme", 100));
        store.TryReserveTokens(Rules(("t:acme", 100)), 80, Now).IsAcquired.Should().BeTrue();

        var refused = store.TryReserveTokens(both, 50, Now);

        refused.IsAcquired.Should().BeFalse();
        refused.Scope.Should().Be(RateLimitScope.Tenant);
        store.TryReserveTokens(model, 1000, Now).IsAcquired.Should().BeTrue("the model bucket was given its 50 back");
    }

    [Fact]
    public void SettleTokens_WhenLessWasUsed_HandsBackTheDifference()
    {
        var store = new InMemoryDistributedRateLimitStore();
        var rules = Rules(("t:acme", 1000));
        store.TryReserveTokens(rules, 900, Now).IsAcquired.Should().BeTrue();

        store.SettleTokens(rules, reserved: 900, actual: 100, Now);

        store.TryReserveTokens(rules, 900, Now).IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void SettleTokens_WhenTheRequestWasNeverServed_HandsBackAllOfIt()
    {
        var store = new InMemoryDistributedRateLimitStore();
        var rules = Rules(("t:acme", 1000));
        store.TryReserveTokens(rules, 1000, Now).IsAcquired.Should().BeTrue();

        store.SettleTokens(rules, reserved: 1000, actual: 0, Now);

        store.TryReserveTokens(rules, 1000, Now).IsAcquired.Should().BeTrue();
    }

    /// <summary>
    /// The backend already spent the tokens. Not counting an overrun would let a caller spend past
    /// its limit by declaring a small <c>max_tokens</c> on a large prompt.
    /// </summary>
    [Fact]
    public void SettleTokens_WhenMoreWasUsed_TakesTheOverrunAndTheBucketOwesIt()
    {
        var store = new InMemoryDistributedRateLimitStore();
        var rules = Rules(("t:acme", 600));
        store.TryReserveTokens(rules, 100, Now).IsAcquired.Should().BeTrue();

        store.SettleTokens(rules, reserved: 100, actual: 900, Now);

        // 600 - 900 = 300 owed, at 10 tokens a second: 31 seconds until one token is there again.
        store.TryReserveTokens(rules, 1, Now.AddSeconds(30)).IsAcquired.Should().BeFalse();
        store.TryReserveTokens(rules, 1, Now.AddSeconds(31)).IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void SettleTokens_DebtIsBoundedAtOneWindow()
    {
        var store = new InMemoryDistributedRateLimitStore();
        var rules = Rules(("t:acme", 600));
        store.TryReserveTokens(rules, 600, Now).IsAcquired.Should().BeTrue();

        store.SettleTokens(rules, reserved: 600, actual: 1_000_000, Now);

        // Owes 600 at most, so one window clears the debt and the next fills the bucket.
        store.TryReserveTokens(rules, 600, Now.AddSeconds(120)).IsAcquired.Should().BeTrue();
    }

    /// <summary>
    /// A request larger than a whole minute's allowance would otherwise never be admitted, however
    /// long the caller waited.
    /// </summary>
    [Fact]
    public void TryReserveTokens_LargerThanTheWholeBudget_IsAdmittedOnAFullBucketOnly()
    {
        var store = new InMemoryDistributedRateLimitStore();
        var rules = Rules(("t:acme", 1000));

        store.TryReserveTokens(rules, 5000, Now).IsAcquired.Should().BeTrue();
        store.TryReserveTokens(rules, 5000, Now.AddSeconds(30)).IsAcquired.Should().BeFalse();

        // Settling hands back only what the rule held, never the 5000 that was asked for.
        store.SettleTokens(rules, reserved: 5000, actual: 0, Now.AddSeconds(30));
        store.TryReserveTokens(rules, 1000, Now.AddSeconds(30)).IsAcquired.Should().BeTrue();
        store.TryReserveTokens(rules, 1, Now.AddSeconds(30)).IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void TryReserveTokens_SkipsRulesWithNoTokenLimit()
    {
        var store = new InMemoryDistributedRateLimitStore();
        var rules = new RateLimitRule[]
        {
            new(RateLimitScope.Tenant, "t:acme", new RateLimitPolicy(10, 0, 0)),
        };

        store.TryReserveTokens(rules, 1_000_000, Now).Should().BeSameAs(RateLimitAcquireResult.Unlimited);
    }

    /// <summary>The two buckets of one partition hold different units and must not share a fill.</summary>
    [Fact]
    public void TokenBucket_IsSeparateFromTheRequestBucket()
    {
        var store = new InMemoryDistributedRateLimitStore();
        var rules = new RateLimitRule[]
        {
            new(RateLimitScope.Tenant, "t:acme", new RateLimitPolicy(2, 0, 0) { Tpm = 1000 }),
        };

        store.TryReserveTokens(rules, 1000, Now).IsAcquired.Should().BeTrue();

        store.TryAcquireAll(rules, Now).IsAcquired.Should().BeTrue();
        store.TryAcquireAll(rules, Now).IsAcquired.Should().BeTrue();
        store.TryAcquireAll(rules, Now).IsAcquired.Should().BeFalse();
        store.TryReserveTokens(rules, 1, Now).IsAcquired.Should().BeFalse();
    }

    private static RateLimitRule[] Rules(params (string Key, int Tpm)[] entries) =>
        entries
            .Select(e => new RateLimitRule(
                e.Key.StartsWith("m:", StringComparison.Ordinal) ? RateLimitScope.Model : RateLimitScope.Tenant,
                e.Key,
                new RateLimitPolicy(0, 0, 0) { Tpm = e.Tpm }))
            .ToArray();
}
