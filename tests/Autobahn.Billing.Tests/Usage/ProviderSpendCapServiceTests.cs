using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Autobahn.Billing.Usage;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Configuration;
using Autobahn.Domain.Identity;

namespace Autobahn.Billing.Tests.Usage;

public sealed class ProviderSpendCapServiceTests
{
    private const string Provider = "openrouter";
    private const string Model = "qwen/qwen3-27b";
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid KeyId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherKeyId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task WithNoCap_EveryRequestIsAllowed()
    {
        var harness = new Harness();
        harness.SetAccess(Access(null, null), Access(KeyId, null));
        harness.SetSpend(Spent(KeyId, 9_999m));

        (await harness.ReserveAsync("r1")).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task AKeyThatHasSpentItsCap_IsRefused_AndToldWhoseCapItWas()
    {
        var harness = new Harness();
        harness.SetAccess(Access(null, 100m), Access(KeyId, 5m));
        harness.SetSpend(Spent(KeyId, 5m));

        var decision = await harness.ReserveAsync("r1");

        decision.IsAllowed.Should().BeFalse();
        decision.Scope.Should().Be(ProviderSpendScope.ApiKey);
        decision.CapUsd.Should().Be(5m);
        decision.SpentUsd.Should().Be(5m);
    }

    /// <summary>The tenant's cap is across all of its keys, so another key's spend counts against it.</summary>
    [Fact]
    public async Task ATenantCapSpentByAnotherKey_RefusesThisOne()
    {
        var harness = new Harness();
        harness.SetAccess(Access(null, 20m), Access(KeyId, null));
        harness.SetSpend(Spent(OtherKeyId, 20m));

        var decision = await harness.ReserveAsync("r1");

        decision.IsAllowed.Should().BeFalse();
        decision.Scope.Should().Be(ProviderSpendScope.Tenant);
        decision.CapUsd.Should().Be(20m);
    }

    /// <summary>
    /// 1 USD per million tokens both ways and a 400,000 byte prompt: each request reserves about
    /// 0.104 USD, so a 0.25 USD cap admits two together and refuses the third.
    /// </summary>
    [Fact]
    public async Task RequestsInFlightTogether_AreHeldToTheCapBetweenThem()
    {
        var harness = new Harness();
        harness.SetAccess(Access(null, null), Access(KeyId, 0.25m));
        harness.SetPrice(1m, 1m);

        (await harness.ReserveAsync("r1", bodyBytes: 400_000)).IsAllowed.Should().BeTrue();
        (await harness.ReserveAsync("r2", bodyBytes: 400_000)).IsAllowed.Should().BeTrue();
        (await harness.ReserveAsync("r3", bodyBytes: 400_000)).IsAllowed.Should().BeFalse();

        harness.Service.Release("r1");

        (await harness.ReserveAsync("r4", bodyBytes: 400_000)).IsAllowed.Should().BeTrue();
    }

    /// <summary>
    /// Spend and caps are cached; the writer invalidates after it has written, before it releases.
    /// </summary>
    [Fact]
    public async Task Invalidate_MakesTheNextRequestSeeWhatWasJustWritten()
    {
        var harness = new Harness();
        harness.SetAccess(Access(null, null), Access(KeyId, 5m));
        harness.SetSpend(Spent(KeyId, 1m));
        (await harness.ReserveAsync("r1")).IsAllowed.Should().BeTrue();

        harness.SetSpend(Spent(KeyId, 5m));
        (await harness.ReserveAsync("r2")).IsAllowed.Should().BeTrue("the cached figure still says 1 USD");

        harness.Service.Invalidate(TenantId, Provider);

        (await harness.ReserveAsync("r3")).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task ACapOnAnotherProvider_DoesNotHoldThisOne()
    {
        var harness = new Harness();
        harness.SetAccess(new ProviderAccessRecord(Guid.NewGuid(), TenantId, KeyId, "deepseek", true, 0m));

        (await harness.ReserveAsync("r1")).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task GetMonthSpend_SumsTheMonthPerKeyAndForTheTenant()
    {
        var harness = new Harness();
        harness.SetSpend(Spent(KeyId, 2m), Spent(KeyId, 3m), Spent(OtherKeyId, 10m));

        var spend = await harness.Service.GetMonthSpendAsync(TenantId, Provider);

        spend.TenantTotal.Should().Be(15m);
        spend.ForKey(KeyId).Should().Be(5m);
        spend.ForKey(Guid.NewGuid()).Should().Be(0m);
    }

    [Fact]
    public void EstimateMaxCost_CountsThePromptTheOutputCeilingAndThePerRequestPrice()
    {
        var card = new RateCardRecord(
            Guid.NewGuid(), "s", "n", Model, 2m, 6m, "USD", DateTimeOffset.UtcNow, null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        {
            PricePerRequest = 0.01m,
        };

        // 4,000,000 bytes is about 1,000,000 prompt tokens at 2 USD; 500,000 output tokens at 6 USD.
        var estimate = ProviderSpendCapService.EstimateMaxCost(card, 500_000, 4_000_000, defaultMaxTokens: 4096);

        estimate.Should().Be(2m + 3m + 0.01m);
    }

    private static ProviderAccessRecord Access(Guid? apiKeyId, decimal? cap) =>
        new(Guid.NewGuid(), TenantId, apiKeyId, Provider, true, cap);

    private static ProviderSpendRecord Spent(Guid apiKeyId, decimal cost) =>
        new(DateOnly.FromDateTime(DateTime.UtcNow), TenantId, apiKeyId, Provider, Model, cost, 1);

    /// <summary>
    /// In the provider's peak hours a request is held against the cap at the peak price, so a cap
    /// with room for one off-peak request has none for the same request at peak.
    /// </summary>
    [Theory]
    [InlineData("2026-10-20T12:00:00Z", true)]
    [InlineData("2026-10-20T02:00:00Z", false)]
    public async Task TheReservation_IsAtThePeakPrice_InPeakHours(string nowUtc, bool allowed)
    {
        var now = DateTimeOffset.Parse(nowUtc, System.Globalization.CultureInfo.InvariantCulture);
        var policy = new PeakPricingPolicy(new Dictionary<string, PeakPricingOptions>
        {
            [Provider] = new() { Multiplier = 2m, Days = [DayOfWeek.Tuesday], WindowsUtc = ["01:00-04:00"] },
        });
        var harness = new Harness(() => now, policy);
        harness.SetAccess(Access(null, 0.006m), Access(KeyId, null));
        harness.SetPrice(input: 0m, output: 1m); // 4,000 output tokens: 0.004 off-peak, 0.008 at peak

        (await harness.ReserveAsync("r1")).IsAllowed.Should().Be(allowed);
    }

    private sealed class Harness
    {
        private readonly IProviderAccessRepository _access = Substitute.For<IProviderAccessRepository>();
        private readonly IProviderSpendRepository _spend = Substitute.For<IProviderSpendRepository>();
        private readonly IRateCardRepository _rateCards = Substitute.For<IRateCardRepository>();

        public Harness(Func<DateTimeOffset>? clock = null, PeakPricingPolicy? peakPricing = null)
        {
            SetAccess();
            SetSpend();
            var services = new ServiceCollection()
                .AddScoped(_ => _access)
                .AddScoped(_ => _spend)
                .AddScoped(_ => _rateCards)
                .BuildServiceProvider();
            Service = new ProviderSpendCapService(
                services.GetRequiredService<IServiceScopeFactory>(),
                new ProviderSpendLedger(TimeSpan.FromMinutes(5)),
                Options.Create(new BillingOptions()),
                clock,
                peakPricing);
        }

        public ProviderSpendCapService Service { get; }

        public void SetAccess(params ProviderAccessRecord[] records) =>
            _access.ListByTenantAsync(TenantId, Arg.Any<CancellationToken>()).Returns(records);

        public void SetSpend(params ProviderSpendRecord[] rows) =>
            _spend.ListAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Provider, TenantId, Arg.Any<CancellationToken>())
                .Returns(rows);

        public void SetPrice(decimal input, decimal output) =>
            _rateCards.GetActiveForModelAsync(Model, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new RateCardRecord(
                    Guid.NewGuid(), "s", "n", Model, input, output, "USD",
                    DateTimeOffset.UtcNow, null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        public ValueTask<ProviderSpendDecision> ReserveAsync(string requestId, long bodyBytes = 400) =>
            Service.TryReserveAsync(TenantId, KeyId, Provider, requestId, Model, requestedMaxTokens: 4000, bodyBytes);
    }
}
