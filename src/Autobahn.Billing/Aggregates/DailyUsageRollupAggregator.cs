using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;

namespace Autobahn.Billing.Aggregates;

public sealed class DailyUsageRollupAggregator : IDailyUsageRollupAggregator
{
    public IReadOnlyList<DailyUsageRollupRecord> Aggregate(IEnumerable<BillingEventRecord> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events
            .GroupBy(DailyUsageRollupKey.FromEvent)
            .Select(group => new DailyUsageRollupRecord(
                group.Key.UsageDate,
                group.Key.TenantId,
                group.Key.ModelId,
                group.Key.CostCenter,
                group.Sum(e => e.PromptTokens),
                group.Sum(e => e.CompletionTokens),
                group.Sum(e => e.TotalCost ?? 0m),
                group.Count()))
            .OrderBy(r => r.UsageDate)
            .ThenBy(r => r.TenantId)
            .ThenBy(r => r.ModelId, StringComparer.Ordinal)
            .ThenBy(r => r.CostCenter, StringComparer.Ordinal)
            .ToList();
    }
}
