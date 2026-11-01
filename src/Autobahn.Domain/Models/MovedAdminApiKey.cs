namespace Autobahn.Domain.Models;
public sealed class MovedAdminApiKey
{
    public Guid Id { get; init; }
    public string KeyPrefix { get; init; } = string.Empty;
    public string? Label { get; init; }
    public Guid FromTenantId { get; init; }
    public string? FromTenantSlug { get; init; }
}
