namespace Autobahn.Domain.Models;
/// <summary>
/// Replaces the tenant's editable fields as a set: a null <see cref = "PlanSlug"/> or
/// <see cref = "CostCenter"/> clears the stored value.
/// </summary>
public sealed class UpdateAdminTenantRequest
{
    public string? Name { get; init; }
    public string? PlanSlug { get; init; }
    public string? CostCenter { get; init; }
}
