using Autobahn.Domain.Identity;

namespace Autobahn.Persistence.Entities;

public sealed class ApiKeyModelGrantEntity
{
    public Guid Id { get; set; }

    public Guid ApiKeyId { get; set; }

    public ApiKeyEntity ApiKey { get; set; } = null!;

    public required string ModelPattern { get; set; }

    public GrantEffect Effect { get; set; } = GrantEffect.Allow;
}
