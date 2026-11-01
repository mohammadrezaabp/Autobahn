namespace Autobahn.Domain.Models.Overview;
public sealed record ActivityEntry
{
    public DateTimeOffset TimestampUtc { get; init; }
    public required string Action { get; init; }
    public string? TenantId { get; init; }
    public string? TenantSlug { get; init; }
    public string? ApiKeyId { get; init; }
    public string? ApiKeyLabel { get; init; }
    /// <summary>The action's details, re-serialised as compact JSON.</summary>
    public string? Details { get; init; }
}
