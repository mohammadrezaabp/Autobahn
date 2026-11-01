namespace Autobahn.Domain.Models;
public sealed class AdminTenantListItem
{
    public Guid Id { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? PlanSlug { get; init; }
    public string? CostCenter { get; init; }
    public bool IsActive { get; init; }
    /// <summary>Admin keys of this tenant hold the gateway-wide Operator policy.</summary>
    public bool IsOperatorTenant { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    /// <summary>Keys the tenant owns, archived ones excluded.</summary>
    public int KeyCount { get; init; }
    /// <summary>Of <see cref = "KeyCount"/>, those that can still authenticate.</summary>
    public int ActiveKeyCount { get; init; }
}
