using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class CreateAdminApiKeyRequest
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ApiKeyRole Role { get; init; } = ApiKeyRole.Inference;
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public string? Label { get; init; }
    public string? Assignee { get; init; }
    public string? Description { get; init; }
    public string? CostCenter { get; init; }
    /// <summary>
    /// Optional absolute expiry, in UTC. Omit for a key that never expires.
    /// </summary>
    /// <remarks>
    /// The <c>ExpiresAt</c> column, the <c>Expired</c> validation failure and the
    /// <c>expired_api_key</c> error code all existed and were all unreachable: creation hardcoded a
    /// null expiry and no update could set one, so keys were revoke-only and every credential issued
    /// was effectively permanent.
    /// </remarks>
    public DateTimeOffset? ExpiresAt { get; init; }
}
