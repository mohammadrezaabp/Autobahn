using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;

namespace Autobahn.Billing.Usage;

/// <summary>
/// Prices a model's stored usage again at its current rate card, for when the card was wrong. The
/// ledger keeps each request's token counts, cached prompt tokens included, so the new cost is what
/// the request would have cost had the card been right when it ran.
/// </summary>
/// <remarks>
/// A row whose cost the upstream reported is left alone: that is the provider's own charge. The
/// monthly caps and budgets read their spend through short caches; those are dropped for every
/// tenant touched, so the new figures apply on the next request.
/// </remarks>
public sealed class BillingRepriceService(
    IBillingRepriceStore store,
    IRateCardRepository rateCards,
    IRateCardCostCalculator calculator,
    IModelRegistry registry,
    BudgetSpendCache budgetSpendCache,
    IProviderSpendCapService spendCaps,
    PeakPricingPolicy? peakPricing = null) : IBillingRepriceService
{
    private readonly PeakPricingPolicy _peakPricing = peakPricing ?? PeakPricingPolicy.None;

    public const int MaxRangeDays = 366;

    public async Task<BillingRepriceReport> RepriceAsync(BillingRepriceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new BillingRepriceException("modelId is required.");
        }

        if (request.From > request.To)
        {
            throw new BillingRepriceException("'from' must be on or before 'to'.");
        }

        if (request.To.DayNumber - request.From.DayNumber + 1 > MaxRangeDays)
        {
            throw new BillingRepriceException($"A reprice spans at most {MaxRangeDays} days.");
        }

        // Rate cards are keyed by the canonical id; an alias or a different case finds the same card.
        // A model since removed from routing is repriced under the id its rows carry.
        var modelId = registry.TryGetModel(request.ModelId.Trim(), out var model) && model is not null
            ? model.Id
            : request.ModelId.Trim();

        var rateCard = await rateCards.GetActiveForModelAsync(modelId, DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new BillingRepriceException($"Model '{modelId}' has no rate card to price its usage with.", 404);

        var fromUtc = new DateTimeOffset(request.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(request.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var events = await store.ListEventsAsync(modelId, fromUtc, toUtc, cancellationToken).ConfigureAwait(false);

        var updates = new List<BillingEventCostUpdate>();
        var priced = new List<(BillingEventRecord Event, decimal NewTotal)>(events.Count);
        int skippedReported = 0, skippedNoTokens = 0, peakEvents = 0;
        long prompt = 0, cached = 0, completion = 0;

        foreach (var row in events)
        {
            var oldTotal = row.TotalCost ?? 0m;
            prompt += row.PromptTokens;
            cached += row.CachedPromptTokens;
            completion += row.CompletionTokens;

            if (string.Equals(row.CostSource, BillingCostSources.Reported, StringComparison.Ordinal))
            {
                skippedReported++;
                priced.Add((row, oldTotal));
                continue;
            }

            if (row.PromptTokens <= 0 && row.CompletionTokens <= 0)
            {
                skippedNoTokens++;
                priced.Add((row, oldTotal));
                continue;
            }

            // Priced as the persistence handler priced it: at the serving provider's peak rate when
            // the request started in its peak hours.
            var startedAt = PeakPricingPolicy.StartOf(row.RecordedAt, row.DurationMs);
            if (_peakPricing.MultiplierAt(row.Provider, startedAt) != 1m)
            {
                peakEvents++;
            }

            var cost = calculator.Calculate(
                _peakPricing.Apply(rateCard, row.Provider, startedAt),
                row.PromptTokens,
                row.CompletionTokens,
                row.CachedPromptTokens);
            priced.Add((row, cost.TotalCost));
            if (cost.TotalCost != row.TotalCost || cost.InputCost != row.InputCost || cost.OutputCost != row.OutputCost)
            {
                updates.Add(new BillingEventCostUpdate(row.Id, cost.InputCost, cost.OutputCost, cost.TotalCost));
            }
        }

        var applied = false;
        if (!request.DryRun && updates.Count > 0)
        {
            await store.ApplyAsync(modelId, request.From, request.To, updates, cancellationToken).ConfigureAwait(false);
            applied = true;

            foreach (var tenant in events.Where(e => e.TenantId is not null).Select(e => e.TenantId!.Value).Distinct())
            {
                budgetSpendCache.Invalidate(tenant);
            }

            foreach (var group in events
                         .Where(e => e.TenantId is not null && !string.IsNullOrWhiteSpace(e.Provider) && ModelProviders.IsPaid(e.Provider!))
                         .Select(e => (Tenant: e.TenantId!.Value, Provider: e.Provider!.ToLowerInvariant()))
                         .Distinct())
            {
                spendCaps.Invalidate(group.Tenant, group.Provider);
            }
        }

        return new BillingRepriceReport
        {
            ModelId = modelId,
            From = request.From,
            To = request.To,
            DryRun = request.DryRun,
            Applied = applied,
            RateCard = rateCard,
            Events = events.Count,
            Repriced = updates.Count,
            SkippedReported = skippedReported,
            SkippedNoTokens = skippedNoTokens,
            PeakEvents = peakEvents,
            PromptTokens = prompt,
            CachedPromptTokens = cached,
            CompletionTokens = completion,
            OldTotalUsd = priced.Sum(p => p.Event.TotalCost ?? 0m),
            NewTotalUsd = priced.Sum(p => p.NewTotal),
            Daily = priced
                .GroupBy(p => DateOnly.FromDateTime(p.Event.RecordedAt.UtcDateTime))
                .OrderBy(g => g.Key)
                .Select(g => new BillingRepriceDay(g.Key, g.Count(), g.Sum(p => p.Event.TotalCost ?? 0m), g.Sum(p => p.NewTotal)))
                .ToList(),
            ByTenant = priced
                .GroupBy(p => p.Event.TenantId)
                .Select(g => new BillingRepriceTenant(g.Key, g.Count(), g.Sum(p => p.Event.TotalCost ?? 0m), g.Sum(p => p.NewTotal)))
                .OrderByDescending(t => t.OldUsd)
                .ToList(),
        };
    }
}
