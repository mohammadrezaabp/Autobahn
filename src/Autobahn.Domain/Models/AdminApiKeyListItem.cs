using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class AdminApiKeyListItem
{
    public required Guid Id { get; init; }
    public required string KeyPrefix { get; init; }
    public required ApiKeyRole Role { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public string? Label { get; init; }
    public string? Assignee { get; init; }
    public string? Description { get; init; }
    public string? CostCenter { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
    public ApiKeyUsageSummary? UsageSummary { get; init; }
    public bool IsRevoked => RevokedAt is not null;
    public bool IsArchived => ArchivedAt is not null;
    /// <summary>
    /// Whether the key has ever authenticated a request or left a billing or error record.
    /// </summary>
    public bool HasUsage { get; init; }
    /// <summary>
    /// Whether this key is eligible for permanent deletion right now: revoked, never used, and not
    /// the caller's own key.
    /// </summary>
    /// <remarks>
    /// Computed server-side and shipped with the list so the console can render the right affordance
    /// without a second round trip, and — more importantly — without a second copy of the eligibility
    /// rule in JavaScript that could drift from the one the endpoint enforces.
    /// </remarks>
    public bool CanDelete { get; init; }
    /// <summary>Whether the key can be archived right now: revoked and not already archived.</summary>
    public bool CanArchive => IsRevoked && !IsArchived;
}
