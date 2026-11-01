namespace Autobahn.Domain.Models.Overview;
public sealed record TenantConsumer
{
    public Guid? TenantId { get; init; }
    public string? TenantSlug { get; init; }
    public string? PlanSlug { get; init; }
    public long Requests { get; init; }
    public long PromptTokens { get; init; }
    public long CompletionTokens { get; init; }
    public decimal Cost { get; init; }
    /// <summary>Requests in the last 24 hours, from the in-memory tenant activity ring (0 after a restart).</summary>
    public long Requests24h { get; init; }
}
