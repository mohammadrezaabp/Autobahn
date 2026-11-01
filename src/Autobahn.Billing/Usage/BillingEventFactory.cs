using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;

namespace Autobahn.Billing.Usage;

public static class BillingEventFactory
{
    public static BillingEventRecord FromUsageEvent(
        UsageEvent usageEvent,
        BillingCostBreakdown? costs = null,
        Guid? billingEventId = null)
    {
        ArgumentNullException.ThrowIfNull(usageEvent);

        if (string.IsNullOrWhiteSpace(usageEvent.RequestId))
        {
            throw new ArgumentException("RequestId is required for idempotent usage events.", nameof(usageEvent));
        }

        Guid? tenantId = Guid.TryParse(usageEvent.TenantId, out var parsedTenantId)
            ? parsedTenantId
            : null;
        Guid? apiKeyId = Guid.TryParse(usageEvent.ApiKeyId, out var parsedApiKeyId)
            ? parsedApiKeyId
            : null;

        // Guard against an unset timestamp: a default(DateTimeOffset) would otherwise record the event
        // and its daily rollup under year 0001, hiding it from current-period usage and budget totals.
        var recordedAt = usageEvent.TimestampUtc == default
            ? DateTimeOffset.UtcNow
            : usageEvent.TimestampUtc;

        return new BillingEventRecord(
            billingEventId ?? Guid.NewGuid(),
            usageEvent.RequestId,
            tenantId,
            apiKeyId,
            usageEvent.ModelId,
            usageEvent.CostCenter,
            usageEvent.PromptTokens,
            usageEvent.CompletionTokens,
            costs?.InputCost,
            costs?.OutputCost,
            costs?.TotalCost,
            usageEvent.DurationMs,
            recordedAt)
        {
            Provider = string.IsNullOrWhiteSpace(usageEvent.Provider) ? null : usageEvent.Provider,
            CachedPromptTokens = usageEvent.CachedPromptTokens,
            CostSource = costs is null
                ? null
                : usageEvent.ReportedCost is not null ? BillingCostSources.Reported : BillingCostSources.RateCard,
        };
    }
}
