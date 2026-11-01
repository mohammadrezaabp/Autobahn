using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Domain.RateLimiting;
using Autobahn.Observability.Metrics;
using Autobahn.Observability.RateLimiting;
using Autobahn.Observability.Runtime;

namespace Autobahn.Observability.Tests.Metrics;

/// <summary>
/// The series added for the alerts and dashboards of the monitoring proposal: who answered a
/// forward, how long the upstream held it, how close a limit is to binding, and what the process is.
/// </summary>
public sealed class Phase1bMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 30, TimeSpan.Zero);

    [Fact]
    public void RecordForwardAttempt_CarriesTheUpstreamStatus()
    {
        var seen = Capture<long>("gateway_forward_attempts_total", collector =>
        {
            collector.RecordForwardAttempt("m1", "upstream_5xx", 503);
            collector.RecordForwardAttempt("m1", "upstream_timeout");
            collector.RecordForwardAttempt("m1", "success", 99);
        });

        seen.Should().Contain(m => Tag(m, "outcome") == "upstream_5xx" && Tag(m, "upstream_status") == "503");
        seen.Should().Contain(m => Tag(m, "outcome") == "upstream_timeout" && Tag(m, "upstream_status") == "none");
        seen.Should().Contain(m => Tag(m, "outcome") == "success" && Tag(m, "upstream_status") == "other");
    }

    [Fact]
    public void RecordUpstreamDurationAndQueueWait_AreRecordedPerModel()
    {
        var upstream = Capture<double>("gateway_upstream_duration_seconds", c => c.RecordUpstreamDuration("m1", 1.5));
        var queue = Capture<double>("gateway_bulkhead_queue_wait_seconds", c => c.RecordBulkheadQueueWait("m1", 0.2));

        upstream.Should().ContainSingle(m => m.Value == 1.5 && Tag(m, "model") == "m1");
        queue.Should().ContainSingle(m => m.Value == 0.2 && Tag(m, "model") == "m1");
    }

    [Fact]
    public void ReadLimitUtilization_IsChargedRateOverTheLimit_ForCompleteMinutesOnly()
    {
        var time = new MutableTimeProvider(Now);
        var tracker = new RateLimitUsageTracker(Options.Create(new RateLimitingOptions()), timeProvider: time);
        var model = Rule(RateLimitScope.Model, "model:m1", "model:m1", rpm: 100);

        // 50 charged in each of five minutes, then a burst in the minute still in progress.
        for (var minute = 0; minute < 5; minute++)
        {
            for (var i = 0; i < 50; i++)
            {
                tracker.RecordRateStage([model], RateLimitStageOutcome.Charged);
            }

            time.Advance(TimeSpan.FromMinutes(1));
        }

        for (var i = 0; i < 90; i++)
        {
            tracker.RecordRateStage([model], RateLimitStageOutcome.Charged);
        }

        var utilization = tracker.ReadLimitUtilization(5, time.GetUtcNow());

        utilization.Should().ContainSingle();
        utilization[0].LimitId.Should().Be("model:m1");
        utilization[0].Ratio.Should().BeApproximately(0.5, 0.0001);
    }

    /// <summary>Every caller of a tier has a bucket of its own, so their sum is not a share of one limit.</summary>
    [Fact]
    public void ReadLimitUtilization_LeavesTiersOut()
    {
        var time = new MutableTimeProvider(Now);
        var tracker = new RateLimitUsageTracker(Options.Create(new RateLimitingOptions()), timeProvider: time);
        tracker.RecordRateStage([Rule(RateLimitScope.Tenant, "t:acme", "default", rpm: 100)], RateLimitStageOutcome.Charged);
        time.Advance(TimeSpan.FromMinutes(1));

        tracker.ReadLimitUtilization(5, time.GetUtcNow()).Should().BeEmpty();
    }

    [Fact]
    public async Task RateLimitExporter_PublishesUtilizationPerLimit()
    {
        var usage = Substitute.For<IRateLimitUsageTracker>();
        usage.ReadLimitUtilization(GatewayRateLimitMetricsExporter.UtilizationWindowMinutes, Arg.Any<DateTimeOffset>())
            .Returns([new RateLimitLimitUtilization("model:m1", 0.9)]);
        var exporter = new GatewayRateLimitMetricsExporter(usage: usage);

        var seen = await ObserveAsync<double>("gateway_rate_limit_utilization_ratio", exporter);

        seen.Should().Contain(m => m.Value == 0.9 && Tag(m, "limit_id") == "model:m1");
    }

    [Fact]
    public async Task ProcessInfoExporter_PublishesStartTimeBuildAndConfigVersion()
    {
        var config = Substitute.For<IGatewayConfigProvider>();
        config.Current.Returns(new GatewayConfigSnapshot { Version = 42 });
        var exporter = new GatewayProcessInfoMetricsExporter(config);

        var start = await ObserveAsync<double>("gateway_process_start_time_seconds", exporter);
        var build = await ObserveAsync<int>("gateway_build_info", exporter);
        var version = await ObserveAsync<long>("gateway_config_version", exporter);

        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        start.Should().Contain(m => m.Value > nowSeconds - 86_400 && m.Value <= nowSeconds + 1);
        build.Should().Contain(m => m.Value == 1 && !string.IsNullOrEmpty(Tag(m, "version")));
        version.Should().Contain(m =>
            m.Value == 42 && Tag(m, "section") == GatewayProcessInfoMetricsExporter.GeneralSection);
    }

    private static RateLimitRule Rule(RateLimitScope scope, string partition, string limitId, int rpm) =>
        new(scope, partition, new RateLimitPolicy(rpm, 0, 0)) { LimitId = limitId, StreamLimitId = limitId };

    private static string? Tag<T>((T Value, Dictionary<string, string?> Tags) measurement, string name) =>
        measurement.Tags.GetValueOrDefault(name);

    private static List<(T Value, Dictionary<string, string?> Tags)> Capture<T>(
        string instrumentName,
        Action<GatewayMetricsCollector> act)
        where T : struct
    {
        var seen = new List<(T, Dictionary<string, string?>)>();
        using var listener = Listen(instrumentName, seen);
        act(new GatewayMetricsCollector(new GatewayRuntimeState()));
        return seen;
    }

    private static async Task<List<(T Value, Dictionary<string, string?> Tags)>> ObserveAsync<T>(
        string instrumentName,
        Microsoft.Extensions.Hosting.IHostedService exporter)
        where T : struct
    {
        var seen = new List<(T, Dictionary<string, string?>)>();
        using var listener = Listen(instrumentName, seen);
        await exporter.StartAsync(CancellationToken.None);
        listener.RecordObservableInstruments();
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
            var copy = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value?.ToString();
            }

            lock (seen)
            {
                seen.Add((measurement, copy));
            }
        });

        listener.Start();
        return listener;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
