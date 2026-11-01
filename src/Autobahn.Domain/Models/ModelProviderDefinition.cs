namespace Autobahn.Domain.Models;
/// <summary>
/// One provider the gateway tells apart: who it is, where it is reached, and what the gateway can
/// read from it.
/// </summary>
/// <param name = "Hosts">A backend address on one of these hosts, or under one, belongs to the provider.</param>
/// <param name = "DefaultEnvVar">The environment variable the company key is read from.</param>
/// <param name = "CatalogUrl">The model list, when the provider publishes one in the OpenAI shape.</param>
/// <param name = "ReportsCost">Whether responses carry the charge in <c>usage.cost</c>.</param>
/// <param name = "Forwardable">
/// False for a provider whose API is not one of the routes the gateway forwards (speech providers:
/// the gateway forwards chat, completions, embeddings and rerank). Such a provider is still gated,
/// capped and listed, so a backend registered at its address is never open by default.
/// </param>
public sealed record ModelProviderDefinition(string Id, string DisplayName, IReadOnlyList<string> Hosts, string UpstreamBaseUrl, string DefaultEnvVar, string? CatalogUrl, bool ReportsCost, ProviderBalanceKind Balance, bool Forwardable = true)
{
    /// <summary>
    /// The provider ends a stream with its usage only when the request asks for it
    /// (<c>stream_options.include_usage</c>), so the gateway asks. DeepSeek and OpenRouter send
    /// it unasked and are left as they are.
    /// </summary>
    public bool StreamUsageOnRequest { get; init; }
    /// <summary>
    /// The <c>service_tier</c> a client's own is replaced with, for a provider that prices by tier
    /// without reporting the charge: the model's price list holds this tier's prices.
    /// </summary>
    public string? PinnedServiceTier { get; init; }
    /// <summary>
    /// The environment variable holding the key the balance is read with, when that is not the
    /// inference key (OpenAI's costs take an admin key). Null reads it with the company key.
    /// </summary>
    public string? BalanceEnvVar { get; init; }
}
