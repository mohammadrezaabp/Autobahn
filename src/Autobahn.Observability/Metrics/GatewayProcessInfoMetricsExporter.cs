using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Autobahn.Application.Abstractions;
using Autobahn.Application.Diagnostics;

namespace Autobahn.Observability.Metrics;

/// <summary>
/// Publishes what the process is, as opposed to what it is doing: when it started, which release it
/// is, and which configuration version it has loaded.
/// </summary>
/// <remarks>
/// A restart resets the open streams, the adaptive factors and every in-memory counter, and a
/// configuration write changes what is enforced. Both precede incidents often enough that an alert
/// and a dashboard annotation should be able to name them, and neither left a trace in the metrics.
/// </remarks>
public sealed class GatewayProcessInfoMetricsExporter(IGatewayConfigProvider? config = null) : IHostedService
{
    /// <summary>The value <c>section</c> takes: the version every configuration write bumps.</summary>
    public const string GeneralSection = "general";

    private static readonly double StartTimeSeconds = ReadStartTimeSeconds();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        GatewayMeters.Meter.CreateObservableGauge(
            "gateway_process_start_time_seconds",
            static () => StartTimeSeconds,
            description: "Start time of the gateway process, in seconds since the Unix epoch");

        GatewayMeters.Meter.CreateObservableGauge(
            "gateway_build_info",
            static () => new Measurement<int>(
                1,
                new KeyValuePair<string, object?>("version", GatewayBuildInfo.Version)),
            description: "Always 1; the version label names the running release");

        GatewayMeters.Meter.CreateObservableGauge(
            "gateway_config_version",
            ObserveConfigVersion,
            description: "Version of the database-backed configuration this process has loaded");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private IEnumerable<Measurement<long>> ObserveConfigVersion()
    {
        if (config is null)
        {
            yield break;
        }

        yield return new Measurement<long>(
            config.Current.Version,
            new KeyValuePair<string, object?>("section", GeneralSection));
    }

    private static double ReadStartTimeSeconds()
    {
        using var process = Process.GetCurrentProcess();
        return new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds() / 1000d;
    }
}
