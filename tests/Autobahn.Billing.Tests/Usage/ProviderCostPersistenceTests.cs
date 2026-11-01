using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Autobahn.Billing.RateCards;
using Autobahn.Billing.Usage;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;

namespace Autobahn.Billing.Tests.Usage;

/// <summary>
/// What a request cost at a paid provider: the provider's own figure first, the model's price as
/// the fallback, and either way added to the spend the monthly caps are held against.
/// </summary>
public sealed class ProviderCostPersistenceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid KeyId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string Model = "qwen/qwen3-27b";

    [Fact]
    public async Task TheReportedCost_IsStored_NotTheRateCardsArithmetic()
    {
        var harness = new Harness(inputPrice: 100m, outputPrice: 100m);

        await harness.Handler.PersistAsync(Event("r1", ModelProviders.OpenRouter, reportedCost: 0.0123m));

        var stored = harness.Appended.Single();
        stored.TotalCost.Should().Be(0.0123m);
        stored.CostSource.Should().Be(BillingCostSources.Reported);
        stored.Provider.Should().Be(ModelProviders.OpenRouter);
    }

    [Fact]
    public async Task WithoutAReportedCost_TheModelsPriceApplies_WithCachedTokensAtTheirOwnRate()
    {
        var harness = new Harness(inputPrice: 2m, outputPrice: 8m, cachedInputPrice: 0.5m);

        await harness.Handler.PersistAsync(
            Event("r1", ModelProviders.DeepSeek, promptTokens: 1_000_000, completionTokens: 500_000, cachedTokens: 600_000));

        var stored = harness.Appended.Single();
        // 400,000 at 2 + 600,000 at 0.5 + 500,000 at 8, per million.
        stored.TotalCost.Should().Be(0.8m + 0.3m + 4m);
        stored.CostSource.Should().Be(BillingCostSources.RateCard);
        stored.CachedPromptTokens.Should().Be(600_000);
    }

    [Fact]
    public async Task AReportedCostWithNoRateCard_IsStillPriced()
    {
        var harness = new Harness(inputPrice: null, outputPrice: null);

        await harness.Handler.PersistAsync(Event("r1", ModelProviders.OpenRouter, reportedCost: 0.002m));

        harness.Appended.Single().TotalCost.Should().Be(0.002m);
    }

    [Fact]
    public async Task PaidSpend_IsAddedToTheProvidersSpend_ThenTheCapReservationIsLetGo()
    {
        var harness = new Harness(inputPrice: 1m, outputPrice: 1m);
        var order = new List<string>();
        harness.Spend
            .When(s => s.IncrementAsync(Arg.Any<IReadOnlyList<ProviderSpendDelta>>(), Arg.Any<CancellationToken>()))
            .Do(_ => order.Add("write"));
        harness.Caps.When(c => c.Invalidate(TenantId, ModelProviders.OpenRouter)).Do(_ => order.Add("invalidate"));
        harness.Caps.When(c => c.Release("r1")).Do(_ => order.Add("release"));

        await harness.Handler.PersistBatchAsync(
        [
            Event("r1", ModelProviders.OpenRouter, reportedCost: 0.25m),
            Event("r2", ModelProviders.OpenRouter, reportedCost: 0.5m),
        ]);

        var deltas = (IReadOnlyList<ProviderSpendDelta>)harness.Spend.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IProviderSpendRepository.IncrementAsync))
            .GetArguments()[0]!;
        var delta = deltas.Single();
        delta.Provider.Should().Be(ModelProviders.OpenRouter);
        delta.TenantId.Should().Be(TenantId);
        delta.ApiKeyId.Should().Be(KeyId);
        delta.ModelId.Should().Be(Model);
        delta.Cost.Should().Be(0.75m);
        delta.RequestCount.Should().Be(2);

        // Written, then the cached figure discarded, and only then the reservation released: at no
        // point is the cost held by neither the spend table nor the ledger.
        order.Take(3).Should().Equal("write", "invalidate", "release");
        harness.Caps.Received(1).Release("r2");
    }

    [Fact]
    public async Task SelfHostedUsage_TouchesNoProviderSpend()
    {
        var harness = new Harness(inputPrice: 1m, outputPrice: 1m);

        await harness.Handler.PersistAsync(Event("r1", ModelProviders.SelfHosted));
        await harness.Handler.PersistAsync(Event("r2", provider: null));

        await harness.Spend.DidNotReceive()
            .IncrementAsync(Arg.Any<IReadOnlyList<ProviderSpendDelta>>(), Arg.Any<CancellationToken>());
        harness.Caps.DidNotReceive().Release(Arg.Any<string>());
    }

    /// <summary>A retried batch appends nothing; its requests must still give their reservations back.</summary>
    [Fact]
    public async Task ADuplicateEvent_ReleasesItsCapReservation_WithoutCountingTwice()
    {
        var harness = new Harness(inputPrice: 1m, outputPrice: 1m, appendSucceeds: false);

        await harness.Handler.PersistAsync(Event("r1", ModelProviders.OpenRouter, reportedCost: 0.25m));

        harness.Caps.Received(1).Release("r1");
        await harness.Spend.DidNotReceive()
            .IncrementAsync(Arg.Any<IReadOnlyList<ProviderSpendDelta>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Calculator_APerRequestPrice_IsChargedOnceBesideTheTokens()
    {
        var card = Card(inputPrice: 0m, outputPrice: 0m) with { PricePerRequest = 0.002m };

        new RateCardCostCalculator().Calculate(card, 0, 0, 0).TotalCost.Should().Be(0.002m);
        new RateCardCostCalculator().CalculateFromTotalTokens(card, 0).TotalCost.Should().Be(0.002m);
    }

    [Fact]
    public void Calculator_WithoutACachedPrice_BillsCachedTokensAsInput()
    {
        var card = Card(inputPrice: 2m, outputPrice: 8m);

        new RateCardCostCalculator().Calculate(card, 1_000_000, 0, cachedPromptTokens: 600_000).TotalCost.Should().Be(2m);
    }

    /// <summary>
    /// DeepSeek charges twice the rate card in its peak hours, and only DeepSeek does: the same
    /// request carried by another provider at the same moment is priced at the card.
    /// </summary>
    [Theory]
    [InlineData(ModelProviders.DeepSeek, "2026-10-20T02:00:00Z", 2)]
    [InlineData(ModelProviders.DeepSeek, "2026-10-20T12:00:00Z", 1)]
    [InlineData(ModelProviders.OpenRouter, "2026-10-20T02:00:00Z", 1)]
    [InlineData(ModelProviders.SelfHosted, "2026-10-20T02:00:00Z", 1)]
    public async Task AtTheServingProvidersPeak_TheTokensCostItsPeakPrice(string provider, string atUtc, int factor)
    {
        var harness = new Harness(inputPrice: 2m, outputPrice: 8m, cachedInputPrice: 0.5m, peakPricing: DeepSeekWeekdayMornings);
        var at = DateTimeOffset.Parse(atUtc, System.Globalization.CultureInfo.InvariantCulture);

        await harness.Handler.PersistAsync(
            Event("r1", provider, promptTokens: 1_000_000, completionTokens: 500_000, cachedTokens: 600_000, timestampUtc: at));

        harness.Appended.Single().TotalCost.Should().Be((0.8m + 0.3m + 4m) * factor);
    }

    /// <summary>The ledger records a request when it ends; the provider prices it by when it started.</summary>
    [Fact]
    public async Task ARequestThatStartedAtPeak_AndEndedAfter_IsAPeakOne()
    {
        var harness = new Harness(inputPrice: 0m, outputPrice: 1m, peakPricing: DeepSeekWeekdayMornings);

        await harness.Handler.PersistAsync(Event(
            "r1",
            ModelProviders.DeepSeek,
            promptTokens: 0,
            completionTokens: 1_000_000,
            timestampUtc: new DateTimeOffset(2026, 10, 20, 4, 1, 0, TimeSpan.Zero),
            durationMs: 120_000));

        harness.Appended.Single().TotalCost.Should().Be(2m);
    }

    private static readonly PeakPricingPolicy DeepSeekWeekdayMornings = new(new Dictionary<string, Autobahn.Domain.Configuration.PeakPricingOptions>
    {
        [ModelProviders.DeepSeek] = new()
        {
            Multiplier = 2m,
            Days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            WindowsUtc = ["01:00-04:00", "06:00-10:00"],
        },
    });

    private static UsageEvent Event(
        string requestId,
        string? provider,
        decimal? reportedCost = null,
        long promptTokens = 100,
        long completionTokens = 50,
        long cachedTokens = 0,
        DateTimeOffset? timestampUtc = null,
        double durationMs = 0) =>
        new()
        {
            RequestId = requestId,
            TenantId = TenantId.ToString(),
            ApiKeyId = KeyId.ToString(),
            ModelId = Model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CachedPromptTokens = cachedTokens,
            ReportedCost = reportedCost,
            Provider = provider,
            TimestampUtc = timestampUtc ?? DateTimeOffset.UtcNow,
            DurationMs = durationMs,
        };

    private static RateCardRecord Card(decimal inputPrice, decimal outputPrice) =>
        new(Guid.NewGuid(), "s", "n", Model, inputPrice, outputPrice, "USD",
            DateTimeOffset.UtcNow.AddDays(-1), null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class Harness
    {
        public Harness(
            decimal? inputPrice,
            decimal? outputPrice,
            decimal? cachedInputPrice = null,
            bool appendSucceeds = true,
            PeakPricingPolicy? peakPricing = null)
        {
            var billingEvents = Substitute.For<IBillingEventRepository>();
            billingEvents.TryAppendAsync(Arg.Any<BillingEventRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    if (appendSucceeds)
                    {
                        Appended.Add(call.Arg<BillingEventRecord>());
                    }

                    return appendSucceeds;
                });

            var rollups = Substitute.For<IDailyUsageRollupRepository>();
            rollups.GetRollupsAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<DailyUsageRollupRecord>());

            var rateCards = Substitute.For<IRateCardRepository>();
            if (inputPrice is { } input && outputPrice is { } output)
            {
                rateCards.GetActiveForModelAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                    .Returns(Card(input, output) with { CachedInputPricePerMillionTokens = cachedInputPrice });
            }

            Handler = new BillingUsagePersistenceHandler(
                billingEvents,
                rollups,
                rateCards,
                new RateCardCostCalculator(),
                Substitute.For<IBudgetRepository>(),
                Substitute.For<IBillingWebhookDispatcher>(),
                new BillingBudgetWarningTracker(),
                new BillingUnpricedModelTracker(),
                Substitute.For<IApiKeyLastUsedTracker>(),
                new BudgetReservationLedger(TimeSpan.FromMinutes(2)),
                NullLogger<BillingUsagePersistenceHandler>.Instance,
                providerSpend: Spend,
                spendCaps: Caps,
                peakPricing: peakPricing);
        }

        public BillingUsagePersistenceHandler Handler { get; }

        public List<BillingEventRecord> Appended { get; } = [];

        public IProviderSpendRepository Spend { get; } = Substitute.For<IProviderSpendRepository>();

        public IProviderSpendCapService Caps { get; } = Substitute.For<IProviderSpendCapService>();
    }
}
