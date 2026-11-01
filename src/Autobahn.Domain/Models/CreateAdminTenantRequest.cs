namespace Autobahn.Domain.Models;
public sealed class CreateAdminTenantRequest
{
    /// <summary>
    /// The tenant's permanent identifier: lowercase letters, digits and hyphens, at most 64
    /// characters. It is the target of <c>tenant</c> rate-limit rules and the <c>tenant</c> metric
    /// label, so it cannot be changed after creation.
    /// </summary>
    public string? Slug { get; init; }
    public string? Name { get; init; }
    /// <summary>A configured rate-limit plan. Omit for the default tier.</summary>
    public string? PlanSlug { get; init; }
    public string? CostCenter { get; init; }
}
