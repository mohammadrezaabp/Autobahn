namespace Autobahn.Domain.Billing;
public sealed record BillingEventRecord(Guid Id, string RequestId, Guid? TenantId, Guid? ApiKeyId, string ModelId, string? CostCenter, long PromptTokens, long CompletionTokens, decimal? InputCost, decimal? OutputCost, decimal? TotalCost, double DurationMs, DateTimeOffset RecordedAt)
{
    /// <summary>Who served the request; null on rows written before providers were recorded.</summary>
    public string? Provider { get; init; }
    /// <summary>Prompt tokens served from the upstream's cache; part of <see cref = "PromptTokens"/>.</summary>
    public long CachedPromptTokens { get; init; }
    /// <summary><see cref = "BillingCostSources"/>: where <see cref = "TotalCost"/> came from. Null when unpriced.</summary>
    public string? CostSource { get; init; }
}
