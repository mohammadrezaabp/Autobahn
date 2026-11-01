using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Autobahn.Application.Providers;

namespace Autobahn.Observability.Metrics;

/// <summary>
/// Publishes what the gateway knows about each paid provider as gauges: remaining credit, the
/// level an operator wants to be warned at, when the credit was last read, and how much of its
/// monthly cap each capped tenant has used, and how far this month's balance fall is from the
/// gateway's own charges. The provider alerts are written against these.
/// </summary>
public sealed class GatewayProviderMetricsExporter(ProviderStatusStore status) : IHostedService
{
    private static int _registered;
    private static ProviderStatusStore? _status;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The newest store wins: a test host that builds a second container must not keep
        // exporting the first one's figures.
        Volatile.Write(ref _status, status);
        if (Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return Task.CompletedTask;
        }

        GatewayMeters.Meter.CreateObservableGauge(
            "gateway_provider_credit_remaining",
            static () => ObserveRemaining(Volatile.Read(ref _status)),
            description: "Credit left at a paid provider, in the currency of the label");

        GatewayMeters.Meter.CreateObservableGauge(
            "gateway_provider_credit_low_threshold",
            static () => ObserveThresholds(Volatile.Read(ref _status)),
            description: "Remaining credit below which an operator wants the low-credit alert");

        GatewayMeters.Meter.CreateObservableGauge(
            "gateway_provider_credit_last_read_timestamp_seconds",
            static () => ObserveLastRead(Volatile.Read(ref _status)),
            description: "Unix time of the last successful credit read; the process start until the first one");

        GatewayMeters.Meter.CreateObservableGauge(
            "gateway_provider_tenant_cap_used_ratio",
            static () => ObserveCapUsage(Volatile.Read(ref _status)),
            description: "A tenant's spend at a paid provider this month divided by its monthly cap");

        GatewayMeters.Meter.CreateObservableGauge(
            "gateway_provider_spend_difference_usd",
            static () => ObserveDifference(Volatile.Read(ref _status)),
            description: "This month's fall in a provider's balance minus the gateway's charges there over the same time");

        GatewayMeters.Meter.CreateObservableGauge(
            "gateway_provider_spend_drifting",
            static () => ObserveDrifting(Volatile.Read(ref _status)),
            description: "1 when that difference is past the configured drift threshold, else 0");

        return Task.CompletedTask;
    }

    public static IEnumerable<Measurement<double>> ObserveDifference(ProviderStatusStore? store)
    {
        if (store is null)
        {
            yield break;
        }

        foreach (var provider in store.Watched)
        {
            if (store.GetReconciliation(provider) is { Unavailable: null } report)
            {
                yield return new Measurement<double>(
                    (double)report.DifferenceUsd,
                    new KeyValuePair<string, object?>("provider", provider));
            }
        }
    }

    public static IEnumerable<Measurement<double>> ObserveDrifting(ProviderStatusStore? store)
    {
        if (store is null)
        {
            yield break;
        }

        foreach (var provider in store.Watched)
        {
            if (store.GetReconciliation(provider) is { Unavailable: null } report)
            {
                yield return new Measurement<double>(
                    report.Drifting ? 1 : 0,
                    new KeyValuePair<string, object?>("provider", provider));
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static IEnumerable<Measurement<double>> ObserveRemaining(ProviderStatusStore? store)
    {
        if (store is null)
        {
            yield break;
        }

        foreach (var provider in store.Watched)
        {
            if (store.GetBalance(provider) is { Remaining: { } remaining } reading)
            {
                yield return new Measurement<double>(
                    (double)remaining,
                    new KeyValuePair<string, object?>("provider", provider),
                    new KeyValuePair<string, object?>("currency", reading.Currency));
            }
        }
    }

    public static IEnumerable<Measurement<double>> ObserveThresholds(ProviderStatusStore? store)
    {
        if (store is null)
        {
            yield break;
        }

        foreach (var provider in store.Watched)
        {
            if (store.GetLowBalance(provider) is { } level)
            {
                yield return new Measurement<double>((double)level, new KeyValuePair<string, object?>("provider", provider));
            }
        }
    }

    public static IEnumerable<Measurement<double>> ObserveLastRead(ProviderStatusStore? store)
    {
        if (store is null)
        {
            yield break;
        }

        foreach (var provider in store.Watched)
        {
            var readAt = store.GetBalance(provider)?.ReadAt ?? store.StartedAt;
            yield return new Measurement<double>(
                readAt.ToUnixTimeSeconds(),
                new KeyValuePair<string, object?>("provider", provider));
        }
    }

    public static IEnumerable<Measurement<double>> ObserveCapUsage(ProviderStatusStore? store)
    {
        if (store is null)
        {
            yield break;
        }

        foreach (var usage in store.CapUsage)
        {
            yield return new Measurement<double>(
                usage.Ratio,
                new KeyValuePair<string, object?>("provider", usage.Provider),
                new KeyValuePair<string, object?>("tenant", usage.TenantSlug));
        }
    }
}
