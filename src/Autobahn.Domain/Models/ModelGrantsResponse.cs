namespace Autobahn.Domain.Models;
public sealed class ModelGrantsResponse
{
    public required IReadOnlyList<string> ModelIds { get; init; }
    /// <summary>
    /// API key only: allow entries that end in <c>*</c> — a prefix (<c>qwen/*</c>) or every model
    /// of a provider (<c>openrouter:*</c>).
    /// </summary>
    public IReadOnlyList<string> Patterns { get; init; } = [];
    /// <summary>API key only: models held back from the key even when a pattern allows them.</summary>
    public IReadOnlyList<string> DeniedModelIds { get; init; } = [];
    /// <summary>
    /// Tenant: true when no explicit tenant allowlist (all registry models allowed at tenant level).
    /// API key: always false — keys require an explicit allowlist; an empty list means no model access.
    /// </summary>
    public bool UsesDefaultAccess { get; init; }
}
