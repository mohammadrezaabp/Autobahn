namespace Autobahn.Domain.Models;
public sealed class ProviderTenantAccessResponse
{
    public required string Provider { get; init; }
    public required IReadOnlyList<ProviderTenantAccessRow> Tenants { get; init; }
}
