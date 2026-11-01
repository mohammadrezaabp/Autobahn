using Autobahn.Domain.Identity;

namespace Autobahn.Persistence.Entities;

public sealed class ModelGrantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public TenantEntity Tenant { get; set; } = null!;

    public required string ModelPattern { get; set; }

    public GrantEffect Effect { get; set; } = GrantEffect.Allow;
}
