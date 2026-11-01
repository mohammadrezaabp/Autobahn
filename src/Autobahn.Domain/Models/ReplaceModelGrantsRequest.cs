namespace Autobahn.Domain.Models;
public sealed class ReplaceModelGrantsRequest
{
    public IReadOnlyList<string> ModelIds { get; init; } = [];
    /// <summary>API key only. See <see cref = "ModelGrantsResponse.Patterns"/>.</summary>
    public IReadOnlyList<string> Patterns { get; init; } = [];
    /// <summary>API key only. See <see cref = "ModelGrantsResponse.DeniedModelIds"/>.</summary>
    public IReadOnlyList<string> DeniedModelIds { get; init; } = [];
    /// <summary>
    /// Required to submit an empty <see cref = "ModelIds"/> for a <em>tenant</em>, because an empty
    /// tenant allowlist means "no ceiling — every model in the registry", not "no access".
    /// </summary>
    /// <remarks>
    /// This exists because the two readings are opposites and the dangerous one was the default.
    /// Clearing the list reads as revoking access, but it actually widens a tenant from its handful
    /// of granted models to everything the gateway routes. Requiring the caller to say so turns a
    /// silent privilege escalation into a deliberate one. Ignored for API-key grants, where an empty
    /// list unambiguously means no access.
    /// </remarks>
    public bool AllowAllModels { get; init; }
}
