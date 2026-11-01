using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class DeleteAdminApiKeyRequest
{
    /// <summary>
    /// Must equal the key's stored prefix. A destructive, irreversible action should not be reachable
    /// by a mis-routed click or a replayed id alone.
    /// </summary>
    public string? ConfirmKeyPrefix { get; init; }
}
