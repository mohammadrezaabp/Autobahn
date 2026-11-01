using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class BatchRevokeAdminApiKeysResponse
{
    public required int RevokedCount { get; init; }
}
