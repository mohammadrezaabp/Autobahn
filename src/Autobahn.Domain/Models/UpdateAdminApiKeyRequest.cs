using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class UpdateAdminApiKeyRequest
{
    public string? Label { get; init; }
    public string? Assignee { get; init; }
    public string? Description { get; init; }
    public string? CostCenter { get; init; }
    /// <summary>
    /// New absolute expiry, in UTC. Only applied when <see cref = "UpdateExpiry"/> is set, so an
    /// ordinary metadata edit cannot clear an expiry by omitting the field.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    /// <summary>Set to apply <see cref = "ExpiresAt"/>; pass it with a null value to remove the expiry.</summary>
    public bool UpdateExpiry { get; init; }
}
