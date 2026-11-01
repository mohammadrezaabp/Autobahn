using System.Diagnostics.Metrics;
using Autobahn.Domain.Models;
using Autobahn.Application.Observability;
using Autobahn.Application.Providers;
using Autobahn.Observability.Metrics;
using Autobahn.Observability.Runtime;

namespace Autobahn.Observability.Tests.Metrics;

/// <summary>
/// The series the provider alerts and the Providers page read: spend and requests per paid
/// provider, refused accounts, cap refusals, standby traffic, and the credit gauges.
/// The meter is shared by every test in the process, so each test uses labels of its own.
/// </summary>
public sealed class ProviderMetricsTests
{
    private static readonly DateTimeOffset ReadAt = new(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void RecordTokenUsage_CarriesTheProvider_AndFallsBackToSelfHosted()
    {
        var seen = Capture<long>("gateway_tokens_total", collector =>
        {
            collector.RecordTokenUsage("pm-tokens-paid", 10, 5, MetricCaller.Anonymous, "openrouter");
            collector.RecordTokenUsage("pm-tokens-local", 3, 0, MetricCaller.Anonymous, " ");
            collector.RecordTokenUsage("pm-tokens-null", 0, 4, MetricCaller.Anonymous, null);
        });

        seen.Should().Contain(m =>
            Tag(m, "model") == "pm-tokens-paid" && Tag(m, "direction") == "total" &&
            Tag(m, "provider") == "openrouter" && m.Value == 15);
        seen.Where(m => Tag(m, "model") == "pm-tokens-local").Should().NotBeEmpty()
            .And.OnlyContain(m => Tag(m, "provider") == ModelProviders.SelfHosted);
        seen.Where(m => Tag(m, "model") == "pm-tokens-null").Should().NotBeEmpty()
            .And.OnlyContain(m => Tag(m, "provider") == ModelProviders.SelfHosted);
    }

    [Fact]
    public void RecordProviderSpend_AddsRequestsAndCostPerProvider()
    {
        var requests = new List<(long Value, Dictionary<string, string?> Tags)>();
        var spend = new List<(double Value, Dictionary<string, string?> Tags)>();
        using var requestListener = Listen("gateway_provider_requests_total", requests);
        using var spendListener = Listen("gateway_provider_spend_dollars_total", spend);

        NewCollector().RecordProviderSpend("pm-spend", 0.25, 3);

        requests.Should().ContainSingle(m => Tag(m, "provider") == "pm-spend" && m.Value == 3);
        spend.Should().ContainSingle(m => Tag(m, "provider") == "pm-spend" && m.Value == 0.25);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RecordProviderSpend_CountsTheRequests_ButNotACostThatIsNotAnAmount(double cost)
    {
        var requests = new List<(long Value, Dictionary<string, string?> Tags)>();
        var spend = new List<(double Value, Dictionary<string, string?> Tags)>();
        using var requestListener = Listen("gateway_provider_requests_total", requests);
        using var spendListener = Listen("gateway_provider_spend_dollars_total", spend);
        var provider = $"pm-bad-cost-{Guid.NewGuid():N}";

        NewCollector().RecordProviderSpend(provider, cost, 2);

        requests.Should().ContainSingle(m => Tag(m, "provider") == provider && m.Value == 2);
        spend.Should().NotContain(m => Tag(m, "provider") == provider);
    }

    [Fact]
    public void RecordProviderSpend_WithoutRequests_AddsOnlyTheCost()
    {
        var requests = new List<(long Value, Dictionary<string, string?> Tags)>();
        var spend = new List<(double Value, Dictionary<string, string?> Tags)>();
        using var requestListener = Listen("gateway_provider_requests_total", requests);
        using var spendListener = Listen("gateway_provider_spend_dollars_total", spend);

        NewCollector().RecordProviderSpend("pm-cost-only", 1.5, 0);

        requests.Should().NotContain(m => Tag(m, "provider") == "pm-cost-only");
        spend.Should().ContainSingle(m => Tag(m, "provider") == "pm-cost-only" && m.Value == 1.5);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RecordProviderSpend_WithoutAProvider_RecordsNothing(string? provider)
    {
        var requests = new List<(long Value, Dictionary<string, string?> Tags)>();
        var spend = new List<(double Value, Dictionary<string, string?> Tags)>();
        using var requestListener = Listen("gateway_provider_requests_total", requests);
        using var spendListener = Listen("gateway_provider_spend_dollars_total", spend);

        NewCollector().RecordProviderSpend(provider!, 7.125, 7125);

        requests.Should().NotContain(m => m.Value == 7125);
        spend.Should().NotContain(m => m.Value == 7.125);
    }

    [Fact]
    public void RecordProviderAccountRefusal_CarriesTheUpstreamStatus()
    {
        var seen = Capture<long>(
            "gateway_provider_account_refusals_total",
            collector => collector.RecordProviderAccountRefusal("pm-refused", 402));

        seen.Should().ContainSingle(m =>
            Tag(m, "provider") == "pm-refused" && Tag(m, "upstream_status") == "402" && m.Value == 1);
    }

    [Fact]
    public void RecordSpendCapRejection_NamesTheScopeTenantAndModel()
    {
        var seen = Capture<long>("gateway_provider_spend_cap_rejections_total", collector =>
        {
            collector.RecordSpendCapRejection("pm-capped", "tenant", "acme", "pm-cap-model");
            collector.RecordSpendCapRejection("pm-capped", "provider", null, "pm-cap-model");
        });

        seen.Should().ContainSingle(m =>
            Tag(m, "provider") == "pm-capped" && Tag(m, "scope") == "tenant" &&
            Tag(m, "tenant") == "acme" && Tag(m, "model") == "pm-cap-model");
        seen.Should().ContainSingle(m =>
            Tag(m, "provider") == "pm-capped" && Tag(m, "scope") == "provider" &&
            Tag(m, "tenant") == "unknown");
    }

    [Fact]
    public void RecordStandbyServed_NamesTheModelAndTheProviderThatCarriedIt()
    {
        var seen = Capture<long>(
            "gateway_standby_served_total",
            collector => collector.RecordStandbyServed("pm-standby-model", "pm-standby"));

        seen.Should().ContainSingle(m =>
            Tag(m, "model") == "pm-standby-model" && Tag(m, "provider") == "pm-standby" && m.Value == 1);
    }

    [Fact]
    public void RecordBilledCost_WithoutAProvider_IsSelfHosted()
    {
        var seen = Capture<double>(
            "gateway_billed_cost_dollars_total",
            collector => collector.RecordBilledCost("acme", "pm-billed-model", "research", 0.5));

        seen.Should().ContainSingle(m =>
            Tag(m, "model") == "pm-billed-model" && Tag(m, "provider") == ModelProviders.SelfHosted && m.Value == 0.5);
    }

    [Fact]
    public void ObserveRemaining_ReportsWatchedProvidersThatHaveAFigure()
    {
        var store = new ProviderStatusStore();
        store.SetWatched("openrouter", true);
        store.SetWatched("deepseek", true);
        store.SetWatched("no-limit", true);
        store.SetWatched("never-read", true);
        store.SetBalance("openrouter", Reading(12.5m, "USD"));
        store.SetBalance("deepseek", Reading(80m, "CNY"));
        store.SetBalance("no-limit", Reading(null, "USD"));
        store.SetBalance("unwatched", Reading(99m, "USD"));

        var measurements = GatewayProviderMetricsExporter.ObserveRemaining(store).ToList();

        measurements.Should().HaveCount(2);
        measurements.Should().ContainSingle(m =>
            m.Value == 12.5 && Tag(m, "provider") == "openrouter" && Tag(m, "currency") == "USD");
        measurements.Should().ContainSingle(m =>
            m.Value == 80 && Tag(m, "provider") == "deepseek" && Tag(m, "currency") == "CNY");
    }

    [Fact]
    public void ObserveThresholds_ReportsOnlyTheLevelsAnOperatorSet()
    {
        var store = new ProviderStatusStore();
        store.SetWatched("openrouter", true);
        store.SetWatched("deepseek", true);
        store.SetLowBalance("openrouter", 5m);

        var measurements = GatewayProviderMetricsExporter.ObserveThresholds(store).ToList();

        measurements.Should().ContainSingle();
        measurements[0].Value.Should().Be(5);
        Tag(measurements[0], "provider").Should().Be("openrouter");
    }

    /// <summary>
    /// A provider that never answered is reported as last read when the process started, so the
    /// "not read for 30 minutes" alert fires for it as well.
    /// </summary>
    [Fact]
    public void ObserveLastRead_FallsBackToTheProcessStart_UntilTheFirstRead()
    {
        var store = new ProviderStatusStore();
        store.SetWatched("openrouter", true);
        store.SetWatched("never-read", true);
        store.SetWatched("failing", true);
        store.SetBalance("openrouter", Reading(1m, "USD"));
        store.SetBalanceError("failing", "timeout");

        var measurements = GatewayProviderMetricsExporter.ObserveLastRead(store).ToList();

        measurements.Should().HaveCount(3);
        measurements.Should().ContainSingle(m =>
            Tag(m, "provider") == "openrouter" && m.Value == ReadAt.ToUnixTimeSeconds());
        measurements.Should().ContainSingle(m =>
            Tag(m, "provider") == "never-read" && m.Value == store.StartedAt.ToUnixTimeSeconds());
        measurements.Should().ContainSingle(m =>
            Tag(m, "provider") == "failing" && m.Value == store.StartedAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void ObserveCapUsage_ReportsEachCappedTenant()
    {
        var store = new ProviderStatusStore();
        store.SetCapUsage(
        [
            new ProviderTenantCapUsage("openrouter", "acme", 0.4),
            new ProviderTenantCapUsage("openrouter", "globex", 1.1),
        ]);

        var measurements = GatewayProviderMetricsExporter.ObserveCapUsage(store).ToList();

        measurements.Should().HaveCount(2);
        measurements.Should().ContainSingle(m =>
            m.Value == 0.4 && Tag(m, "provider") == "openrouter" && Tag(m, "tenant") == "acme");
        measurements.Should().ContainSingle(m => m.Value == 1.1 && Tag(m, "tenant") == "globex");
    }

    [Fact]
    public void Observers_WithoutAStore_ReportNothing()
    {
        GatewayProviderMetricsExporter.ObserveRemaining(null).Should().BeEmpty();
        GatewayProviderMetricsExporter.ObserveThresholds(null).Should().BeEmpty();
        GatewayProviderMetricsExporter.ObserveLastRead(null).Should().BeEmpty();
        GatewayProviderMetricsExporter.ObserveCapUsage(null).Should().BeEmpty();
    }

    /// <summary>
    /// The gauges are registered once per process and read whichever store started last, so a
    /// second host does not keep exporting the first one's figures.
    /// </summary>
    [Fact]
    public async Task StartAsync_RegistersTheGauges_AndTheNewestStoreWins()
    {
        var first = new ProviderStatusStore();
        first.SetWatched("pm-first-host", true);
        first.SetLowBalance("pm-first-host", 1m);
        var second = new ProviderStatusStore();
        second.SetWatched("pm-second-host", true);
        second.SetLowBalance("pm-second-host", 2m);
        second.SetBalance("pm-second-host", Reading(42m, "USD"));
        second.SetCapUsage([new ProviderTenantCapUsage("pm-second-host", "acme", 0.75)]);

        var seen = new List<(double Value, Dictionary<string, string?> Tags, string Instrument)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == GatewayMeters.MeterName &&
                    instrument.Name.StartsWith("gateway_provider_", StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            lock (seen)
            {
                seen.Add((measurement, Copy(tags), instrument.Name));
            }
        });
        listener.Start();

        var firstExporter = new GatewayProviderMetricsExporter(first);
        var secondExporter = new GatewayProviderMetricsExporter(second);
        await firstExporter.StartAsync(CancellationToken.None);
        await secondExporter.StartAsync(CancellationToken.None);
        listener.RecordObservableInstruments();
        await firstExporter.StopAsync(CancellationToken.None);
        await secondExporter.StopAsync(CancellationToken.None);

        seen.Should().Contain(m =>
            m.Instrument == "gateway_provider_credit_remaining" &&
            m.Tags["provider"] == "pm-second-host" && m.Value == 42);
        seen.Should().Contain(m =>
            m.Instrument == "gateway_provider_credit_low_threshold" &&
            m.Tags["provider"] == "pm-second-host" && m.Value == 2);
        seen.Should().Contain(m =>
            m.Instrument == "gateway_provider_credit_last_read_timestamp_seconds" &&
            m.Tags["provider"] == "pm-second-host" && m.Value == ReadAt.ToUnixTimeSeconds());
        seen.Should().Contain(m =>
            m.Instrument == "gateway_provider_tenant_cap_used_ratio" &&
            m.Tags["provider"] == "pm-second-host" && m.Tags["tenant"] == "acme" && m.Value == 0.75);
        seen.Should().NotContain(m => m.Tags["provider"] == "pm-first-host");
    }

    private static ProviderBalanceReading Reading(decimal? remaining, string currency) =>
        new(remaining, Limit: null, Used: null, currency, ReadAt, Error: null);

    private static GatewayMetricsCollector NewCollector() => new(new GatewayRuntimeState());

    private static string? Tag<T>((T Value, Dictionary<string, string?> Tags) measurement, string name) =>
        measurement.Tags.GetValueOrDefault(name);

    private static string? Tag(Measurement<double> measurement, string name)
    {
        foreach (var tag in measurement.Tags)
        {
            if (tag.Key == name)
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }

    private static List<(T Value, Dictionary<string, string?> Tags)> Capture<T>(
        string instrumentName,
        Action<GatewayMetricsCollector> act)
        where T : struct
    {
        var seen = new List<(T, Dictionary<string, string?>)>();
        using var listener = Listen(instrumentName, seen);
        act(NewCollector());
        return seen;
    }

    private static MeterListener Listen<T>(string instrumentName, List<(T, Dictionary<string, string?>)> seen)
        where T : struct
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == GatewayMeters.MeterName && instrument.Name == instrumentName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<T>((_, measurement, tags, _) =>
        {
            lock (seen)
            {
                seen.Add((measurement, Copy(tags)));
            }
        });

        listener.Start();
        return listener;
    }

    private static Dictionary<string, string?> Copy(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value?.ToString();
        }

        return copy;
    }
}
