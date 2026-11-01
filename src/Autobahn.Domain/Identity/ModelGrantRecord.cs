namespace Autobahn.Domain.Identity;

public sealed record ModelGrantRecord(
    Guid Id,
    Guid TenantId,
    string ModelPattern,
    GrantEffect Effect);
