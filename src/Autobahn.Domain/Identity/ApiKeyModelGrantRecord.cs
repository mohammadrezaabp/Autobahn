namespace Autobahn.Domain.Identity;

public sealed record ApiKeyModelGrantRecord(
    Guid Id,
    Guid ApiKeyId,
    string ModelPattern,
    GrantEffect Effect);
