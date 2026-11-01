using System.Collections.Concurrent;
using Autobahn.Domain.Models;

namespace Autobahn.Application.Providers;
/// <summary>
/// What the gateway last learned about each paid provider: its credit, the alert level an
/// operator set, and how close each capped tenant is to its cap. Written by the refresh service,
/// read by the Providers page and the metrics exporter.
/// </summary>
public sealed class ProviderStatusStore
{
    private readonly ConcurrentDictionary<string, ProviderBalanceReading> _balances = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, decimal> _lowBalance = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _watched = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProviderReconciliationReport> _reconciliation = new(StringComparer.OrdinalIgnoreCase);
    private volatile IReadOnlyList<ProviderTenantCapUsage> _capUsage = [];
    /// <summary>
    /// When this process started. A provider whose balance has never been read is reported as last
    /// read then, so "not read for 30 minutes" also fires for one that never answered.
    /// </summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public ProviderBalanceReading? GetBalance(string provider) => _balances.TryGetValue(provider, out var reading) ? reading : null;
    public void SetBalance(string provider, ProviderBalanceReading reading) => _balances[provider] = reading;
    /// <summary>Keeps the last good figures and records why the newest attempt failed.</summary>
    public void SetBalanceError(string provider, string error) => _balances.AddOrUpdate(provider, _ => new ProviderBalanceReading(null, null, null, "USD", null, error), (_, previous) => previous with { Error = error });
    /// <summary>The providers whose balance the gateway is reading: a key is configured and the provider has a balance to read.</summary>
    public IReadOnlyCollection<string> Watched => _watched.Where(p => p.Value).Select(p => p.Key).ToList();

    public void SetWatched(string provider, bool watched)
    {
        _watched[provider] = watched;
        if (!watched)
        {
            _balances.TryRemove(provider, out _);
        }
    }

    public decimal? GetLowBalance(string provider) => _lowBalance.TryGetValue(provider, out var level) ? level : null;
    public void SetLowBalance(string provider, decimal? level)
    {
        if (level is { } value)
        {
            _lowBalance[provider] = value;
        }
        else
        {
            _lowBalance.TryRemove(provider, out _);
        }
    }

    public IReadOnlyList<ProviderTenantCapUsage> CapUsage => _capUsage;

    public void SetCapUsage(IReadOnlyList<ProviderTenantCapUsage> usage) => _capUsage = usage;
    /// <summary>This month's reconciliation of the provider's balance against the gateway's charges, as of the last poll.</summary>
    public ProviderReconciliationReport? GetReconciliation(string provider) => _reconciliation.TryGetValue(provider, out var report) ? report : null;
    public void SetReconciliation(string provider, ProviderReconciliationReport report) => _reconciliation[provider] = report;
}
