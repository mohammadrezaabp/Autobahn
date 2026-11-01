namespace Autobahn.Domain.Models;
public sealed class MoveAdminApiKeysResponse
{
    public Guid TenantId { get; init; }
    public string TenantSlug { get; init; } = string.Empty;
    public IReadOnlyList<MovedAdminApiKey> Moved { get; init; } = [];
    /// <summary>Keys left where they were, each with the reason.</summary>
    public IReadOnlyList<SkippedAdminApiKey> Skipped { get; init; } = [];
}
