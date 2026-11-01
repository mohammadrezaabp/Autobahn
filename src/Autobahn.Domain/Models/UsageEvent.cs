namespace Autobahn.Domain.Models;
public sealed class UsageEvent
{
    public required string RequestId { get; init; }
    public string? TenantId { get; init; }
    public string? ApiKeyId { get; init; }
    /// <summary>
    /// Tenant slug and key label, carried only so the token series can name the caller. Billing
    /// reads the ids above, never these.
    /// </summary>
    public string? TenantSlug { get; init; }
    /// <inheritdoc cref = "TenantSlug"/>
    public string? ApiKeyLabel { get; init; }
    /// <summary>
    /// The key this request's quota was checked under — the tenant id for authenticated traffic,
    /// the per-address anonymous partition (<c>anon:&lt;ip&gt;</c>) for keyless traffic.
    /// </summary>
    /// <remarks>
    /// Usage must be committed back to the same partition the admission check reads, or the check
    /// never sees it. Anonymous usage used to commit under a literal <c>"anonymous"</c> key while
    /// the check read the per-address partition, so keyless callers of public models were never
    /// held to the monthly token quota at all.
    /// </remarks>
    public string? QuotaPartition { get; init; }
    public required string ModelId { get; init; }
    public long PromptTokens { get; init; }
    public long CompletionTokens { get; init; }
    /// <summary>
    /// Combined token count when <see cref = "TokenSource"/> is
    /// <see cref = "UsageTokenSource.TotalOnly"/>; otherwise 0 (the split fields are authoritative).
    /// </summary>
    public long TotalTokens { get; init; }
    /// <summary>
    /// Defaults to <see cref = "UsageTokenSource.Split"/> so existing constructions keep their
    /// meaning; only the parser sets <see cref = "UsageTokenSource.TotalOnly"/>.
    /// </summary>
    public UsageTokenSource TokenSource { get; init; } = UsageTokenSource.Split;
    public double DurationMs { get; init; }
    public string? CostCenter { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    /// <summary>
    /// Who served the request (<see cref = "ModelProviders"/>): the provider of the backend it was
    /// forwarded to, which for a request carried by a standby is not the model's own. Null on
    /// events built before the backend was known; read as self-hosted.
    /// </summary>
    public string? Provider { get; init; }
    /// <summary>
    /// The charge the upstream reported for this request, in USD. When set it is the cost that is
    /// stored; the model's price is only the fallback.
    /// </summary>
    public decimal? ReportedCost { get; init; }
    /// <summary>Prompt tokens the upstream served from its cache; part of <see cref = "PromptTokens"/>.</summary>
    public long CachedPromptTokens { get; init; }
    /// <summary>
    /// Audio tokens on either side, part of the prompt and completion counts. A price list has no
    /// audio price, so they are priced as text; counted so that shows.
    /// </summary>
    public long AudioTokens { get; init; }
}
