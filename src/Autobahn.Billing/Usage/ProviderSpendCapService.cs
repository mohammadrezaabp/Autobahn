using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Configuration;
using Autobahn.Domain.Identity;
using Autobahn.Application.Usage;

namespace Autobahn.Billing.Usage;

/// <summary>
/// Enforces the monthly caps on paid providers: a key's own, and its tenant's.
/// </summary>
/// <remarks>
/// <para>Persisted spend and the caps are read once per tenant and provider and kept for a few
/// seconds. That is safe for the same reason it is for budgets: what has been spent since the last
/// read is in the ledger, and the usage writer calls <see cref="Invalidate"/> before it releases a
/// request's reservation, so no cost is ever held by neither.</para>
///
/// <para>The month is the calendar month in UTC. A provider bills in its own time zone; the cap
/// is the gateway's own promise and only needs to be the same month on every request.</para>
/// </remarks>
public sealed class ProviderSpendCapService(
    IServiceScopeFactory scopeFactory,
    ProviderSpendLedger ledger,
    IOptions<BillingOptions> billingOptions,
    Func<DateTimeOffset>? clock = null,
    PeakPricingPolicy? peakPricing = null) : IProviderSpendCapService
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    private readonly PeakPricingPolicy _peakPricing = peakPricing ?? PeakPricingPolicy.None;
    private readonly ConcurrentDictionary<string, Snapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _generations = new(StringComparer.Ordinal);

    public async ValueTask<ProviderSpendDecision> TryReserveAsync(
        Guid tenantId,
        Guid apiKeyId,
        string provider,
        string requestId,
        string canonicalModelId,
        long? requestedMaxTokens,
        long requestBodyBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return ProviderSpendDecision.Allowed;
        }

        var snapshot = await GetSnapshotAsync(tenantId, provider, cancellationToken).ConfigureAwait(false);
        var tenantCap = snapshot.TenantCap;
        var keyCap = snapshot.KeyCaps.TryGetValue(apiKeyId, out var cap) ? cap : (decimal?)null;

        // Nothing to hold the request against. It is still recorded, so a cap set while it runs
        // counts it.
        var tenantSpent = snapshot.Spend.TenantTotal;
        var keySpent = snapshot.Spend.ForKey(apiKeyId);

        var estimate = tenantCap is null && keyCap is null
            ? 0m
            : await EstimateMaxCostAsync(provider, canonicalModelId, requestedMaxTokens, requestBodyBytes, cancellationToken)
                .ConfigureAwait(false);

        var keyScope = ProviderSpendLedger.KeyScope(apiKeyId, provider);
        var tenantScope = ProviderSpendLedger.TenantScope(tenantId, provider);
        var refusedBy = ledger.TryReserve(
            requestId,
            estimate,
            [
                (keyScope, keyCap is { } k ? k - keySpent : null),
                (tenantScope, tenantCap is { } t ? t - tenantSpent : null),
            ]);

        if (refusedBy is null)
        {
            return ProviderSpendDecision.Allowed;
        }

        return string.Equals(refusedBy, keyScope, StringComparison.Ordinal)
            ? new ProviderSpendDecision(false, ProviderSpendScope.ApiKey, keyCap, keySpent + ledger.GetOutstanding(keyScope))
            : new ProviderSpendDecision(false, ProviderSpendScope.Tenant, tenantCap, tenantSpent + ledger.GetOutstanding(tenantScope));
    }

    public void Release(string requestId) => ledger.Release(requestId);

    public void Invalidate(Guid tenantId, string provider)
    {
        var key = SnapshotKey(tenantId, provider);
        _generations.AddOrUpdate(key, 1, static (_, current) => current + 1);
        _snapshots.TryRemove(key, out _);
    }

    public async Task<ProviderMonthSpend> GetMonthSpendAsync(
        Guid tenantId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await ReadSpendAsync(scope.ServiceProvider, tenantId, provider, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Snapshot> GetSnapshotAsync(Guid tenantId, string provider, CancellationToken cancellationToken)
    {
        var key = SnapshotKey(tenantId, provider);
        var now = _clock();
        var month = MonthStart(now);
        if (_snapshots.TryGetValue(key, out var cached) && cached.ExpiresAt > now && cached.Month == month)
        {
            return cached;
        }

        var generation = _generations.TryGetValue(key, out var g) ? g : 0;

        await using var scope = scopeFactory.CreateAsyncScope();
        var access = scope.ServiceProvider.GetService<IProviderAccessRepository>();
        IReadOnlyList<ProviderAccessRecord> records = access is null
            ? []
            : await access.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        decimal? tenantCap = null;
        var keyCaps = new Dictionary<Guid, decimal>();
        foreach (var record in records)
        {
            if (record.MonthlyCapUsd is not { } capUsd ||
                !string.Equals(record.Provider, provider, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (record.ApiKeyId is { } keyId)
            {
                keyCaps[keyId] = capUsd;
            }
            else
            {
                tenantCap = capUsd;
            }
        }

        var spend = await ReadSpendAsync(scope.ServiceProvider, tenantId, provider, cancellationToken).ConfigureAwait(false);
        var ttl = TimeSpan.FromSeconds(Math.Max(1, billingOptions.Value.BudgetSpendCacheTtlSeconds));
        var snapshot = new Snapshot(month, tenantCap, keyCaps, spend, now + ttl);

        // A read that began before an invalidation saw the figures from before it; serve it to
        // this caller, which raced the change, and do not keep it.
        if ((_generations.TryGetValue(key, out var after) ? after : 0) == generation)
        {
            _snapshots[key] = snapshot;
        }

        return snapshot;
    }

    private async Task<ProviderMonthSpend> ReadSpendAsync(
        IServiceProvider services,
        Guid tenantId,
        string provider,
        CancellationToken cancellationToken)
    {
        var repository = services.GetService<IProviderSpendRepository>();
        if (repository is null)
        {
            return ProviderMonthSpend.Empty;
        }

        var now = _clock();
        var rows = await repository
            .ListAsync(MonthStart(now), DateOnly.FromDateTime(now.UtcDateTime), provider, tenantId, cancellationToken)
            .ConfigureAwait(false);

        var byKey = new Dictionary<Guid, decimal>();
        var total = 0m;
        foreach (var row in rows)
        {
            total += row.Cost;
            if (row.ApiKeyId is { } keyId)
            {
                byKey[keyId] = (byKey.TryGetValue(keyId, out var existing) ? existing : 0m) + row.Cost;
            }
        }

        return new ProviderMonthSpend(total, byKey);
    }

    /// <summary>
    /// An upper bound on what the request can cost, from the model's price: the prompt at the
    /// input rate, the output ceiling at the dearer rate, and the per-request price. 0 for a model
    /// with no price, which then only an already exhausted cap refuses.
    /// </summary>
    private async Task<decimal> EstimateMaxCostAsync(
        string provider,
        string canonicalModelId,
        long? requestedMaxTokens,
        long requestBodyBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(canonicalModelId))
        {
            return 0m;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var rateCards = scope.ServiceProvider.GetService<IRateCardRepository>();
        if (rateCards is null)
        {
            return 0m;
        }

        var now = _clock();
        var rateCard = await rateCards
            .GetActiveForModelAsync(canonicalModelId, now, cancellationToken)
            .ConfigureAwait(false);

        // At the price the request will be charged if it starts now: the peak one in peak hours.
        return rateCard is null
            ? 0m
            : EstimateMaxCost(
                _peakPricing.Apply(rateCard, provider, now),
                requestedMaxTokens,
                requestBodyBytes,
                billingOptions.Value.BudgetReservationDefaultMaxTokens);
    }

    internal static decimal EstimateMaxCost(
        RateCardRecord rateCard,
        long? requestedMaxTokens,
        long requestBodyBytes,
        int defaultMaxTokens)
    {
        var maxOutputTokens = requestedMaxTokens is > 0 ? requestedMaxTokens.Value : defaultMaxTokens;
        var outputRate = Math.Max(rateCard.InputPricePerMillionTokens, rateCard.OutputPricePerMillionTokens);
        var promptTokens = UsageEventFactory.EstimatePromptTokens(requestBodyBytes);
        return decimal.Round(
            (promptTokens / 1_000_000m * rateCard.InputPricePerMillionTokens)
            + (maxOutputTokens / 1_000_000m * outputRate)
            + rateCard.PricePerRequest,
            6);
    }

    private static DateOnly MonthStart(DateTimeOffset now) => new(now.UtcDateTime.Year, now.UtcDateTime.Month, 1);

    private static string SnapshotKey(Guid tenantId, string provider) => $"{tenantId:N}:{provider.ToLowerInvariant()}";

    private sealed record Snapshot(
        DateOnly Month,
        decimal? TenantCap,
        IReadOnlyDictionary<Guid, decimal> KeyCaps,
        ProviderMonthSpend Spend,
        DateTimeOffset ExpiresAt);
}
