using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Autobahn.Billing.RateCards;
using Autobahn.Billing.Usage;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;

namespace Autobahn.Billing.Tests.Usage;

public sealed class BillingRepriceServiceTests
{
    private const string Model = "deepseek-v4-flash";
    private static readonly DateOnly Day = new(2026, 10, 14);
    private static readonly Guid Tenant = Guid.NewGuid();

    // DeepSeek's price: 0.15 input, 0.003 cache hit, 0.60 output per million.
    private static readonly RateCardRecord Card = new(
        Guid.NewGuid(), "ds", "ds", Model, 0.15m, 0.60m, "USD",
        DateTimeOffset.UnixEpoch, null, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
    {
        CachedInputPricePerMillionTokens = 0.003m,
    };

    private readonly IBillingRepriceStore _store = Substitute.For<IBillingRepriceStore>();
    private readonly IProviderSpendCapService _caps = Substitute.For<IProviderSpendCapService>();

    private BillingRepriceService CreateService(params BillingEventRecord[] events) => CreateService(null, events);

    private BillingRepriceService CreateService(PeakPricingPolicy? peakPricing, params BillingEventRecord[] events)
    {
        var rateCards = Substitute.For<IRateCardRepository>();
        rateCards.GetActiveForModelAsync(Model, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Card);
        _store.ListEventsAsync(Model, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(events);

        var registry = Substitute.For<IModelRegistry>();
        registry.TryGetModel(Arg.Any<string>(), out Arg.Any<ModelConfig?>()).Returns(x =>
        {
            x[1] = new ModelConfig { Id = Model };
            return true;
        });

        return new BillingRepriceService(
            _store,
            rateCards,
            new RateCardCostCalculator(),
            registry,
            new BudgetSpendCache(new MemoryCache(new MemoryCacheOptions())),
            _caps,
            peakPricing);
    }

    /// <summary>A row as the gateway priced it at the flat 1 USD per million it had for this model.</summary>
    private static BillingEventRecord Row(long prompt, long cached, long completion, string? source = BillingCostSources.RateCard)
    {
        var old = (prompt + completion) / 1_000_000m;
        return new BillingEventRecord(
            Guid.NewGuid(), Guid.NewGuid().ToString("N"), Tenant, Guid.NewGuid(), Model, null,
            prompt, completion, prompt / 1_000_000m, completion / 1_000_000m, old, 100,
            new DateTimeOffset(Day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero))
        {
            Provider = "deepseek",
            CachedPromptTokens = cached,
            CostSource = source,
        };
    }

    /// <summary>
    /// A row is repriced at the rate it ran at: DeepSeek's peak price for a request that started in
    /// its peak hours, the rate card otherwise, and the rate card for another provider at any hour.
    /// </summary>
    [Fact]
    public async Task ARowThatRanAtThePeak_IsRepricedAtThePeakPrice()
    {
        var policy = new PeakPricingPolicy(new Dictionary<string, Autobahn.Domain.Configuration.PeakPricingOptions>
        {
            ["deepseek"] = new() { Multiplier = 2m, Days = [Day.DayOfWeek], WindowsUtc = ["01:00-04:00"] },
        });
        var peak = Row(1_000_000, 0, 0) with { RecordedAt = At(2, 0) };
        var offPeak = Row(1_000_000, 0, 0) with { RecordedAt = At(12, 0) };
        var otherProvider = Row(1_000_000, 0, 0) with { RecordedAt = At(2, 0), Provider = "openrouter" };
        var service = CreateService(policy, peak, offPeak, otherProvider);

        var report = await service.RepriceAsync(new BillingRepriceRequest(Model, Day, Day, DryRun: false), default);

        report.PeakEvents.Should().Be(1);
        report.NewTotalUsd.Should().Be(0.30m + 0.15m + 0.15m);
        await _store.Received(1).ApplyAsync(
            Model, Day, Day,
            Arg.Is<IReadOnlyList<BillingEventCostUpdate>>(u =>
                u.Single(x => x.EventId == peak.Id).TotalCost == 0.30m &&
                u.Single(x => x.EventId == offPeak.Id).TotalCost == 0.15m),
            Arg.Any<CancellationToken>());
    }

    private static DateTimeOffset At(int hour, int minute) =>
        new(Day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    [Fact]
    public async Task ADryRun_ReportsTheNewCost_AndWritesNothing()
    {
        var service = CreateService(Row(45_290, 44_800, 215));

        var report = await service.RepriceAsync(new BillingRepriceRequest(Model, Day, Day, DryRun: true), default);

        report.OldTotalUsd.Should().Be(0.045505m);
        // 490 at 0.15, 44,800 cached at 0.003, 215 out at 0.60.
        report.NewTotalUsd.Should().Be(0.0000735m + 0.0001344m + 0.000129m);
        report.Repriced.Should().Be(1);
        report.CachedPromptTokens.Should().Be(44_800);
        report.Applied.Should().BeFalse();
        await _store.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default, default, default!, default);
        _caps.DidNotReceiveWithAnyArgs().Invalidate(default, default!);
    }

    [Fact]
    public async Task Applying_WritesOnlyChangedRows_AndDropsTheCapCaches()
    {
        var changed = Row(1_000_000, 0, 0);
        var service = CreateService(changed);

        var report = await service.RepriceAsync(new BillingRepriceRequest(Model, Day, Day, DryRun: false), default);

        report.Applied.Should().BeTrue();
        report.NewTotalUsd.Should().Be(0.15m);
        await _store.Received(1).ApplyAsync(
            Model, Day, Day,
            Arg.Is<IReadOnlyList<BillingEventCostUpdate>>(u => u.Count == 1 && u[0].EventId == changed.Id && u[0].TotalCost == 0.15m),
            Arg.Any<CancellationToken>());
        _caps.Received(1).Invalidate(Tenant, "deepseek");
    }

    [Fact]
    public async Task ARowTheUpstreamPriced_KeepsItsCost()
    {
        var service = CreateService(Row(1_000_000, 0, 0, BillingCostSources.Reported), Row(0, 0, 0));

        var report = await service.RepriceAsync(new BillingRepriceRequest(Model, Day, Day, DryRun: false), default);

        report.SkippedReported.Should().Be(1);
        report.SkippedNoTokens.Should().Be(1);
        report.Repriced.Should().Be(0);
        report.NewTotalUsd.Should().Be(report.OldTotalUsd);
        report.Applied.Should().BeFalse("nothing changed, so nothing was written");
    }

    [Fact]
    public async Task ARangeBackwards_OrTooLong_IsRefused()
    {
        var service = CreateService();

        await FluentActions.Awaiting(() => service.RepriceAsync(new BillingRepriceRequest(Model, Day, Day.AddDays(-1), true), default))
            .Should().ThrowAsync<BillingRepriceException>();
        await FluentActions.Awaiting(() => service.RepriceAsync(new BillingRepriceRequest(Model, Day, Day.AddDays(400), true), default))
            .Should().ThrowAsync<BillingRepriceException>();
    }
}
