namespace Autobahn.Domain.Models;
public sealed class MoveAdminApiKeysRequest
{
    public IReadOnlyList<Guid> KeyIds { get; init; } = [];
}
