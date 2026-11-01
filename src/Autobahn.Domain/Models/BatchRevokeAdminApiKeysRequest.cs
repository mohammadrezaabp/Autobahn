using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class BatchRevokeAdminApiKeysRequest
{
    public IReadOnlyList<Guid> KeyIds { get; init; } = [];
}
