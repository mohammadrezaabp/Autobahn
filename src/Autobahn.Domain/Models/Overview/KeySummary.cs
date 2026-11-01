namespace Autobahn.Domain.Models.Overview;
public sealed record KeySummary
{
    public Guid Id { get; init; }
    public required string KeyPrefix { get; init; }
    public string? Label { get; init; }
    public string? TenantSlug { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
}
