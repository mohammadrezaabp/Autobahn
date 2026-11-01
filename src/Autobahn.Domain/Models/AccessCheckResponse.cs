namespace Autobahn.Domain.Models;
public sealed class AccessCheckResponse
{
    public bool Allowed { get; init; }
    /// <summary>The refusal reason (<c>provider_closed_for_key</c>, …), or null when allowed.</summary>
    public string? Reason { get; init; }
    public required string Message { get; init; }
    /// <summary>The canonical id the name resolved to, or null when no such model is registered.</summary>
    public string? ModelId { get; init; }
    public string? Provider { get; init; }
    /// <summary>The list entry that allowed or denied the model, when one did.</summary>
    public string? Rule { get; init; }
}
