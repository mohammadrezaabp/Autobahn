namespace Autobahn.Domain.Models;
/// <summary>
/// How the gateway reads a paid provider's remaining credit.
/// </summary>
public enum ProviderBalanceKind
{
    /// <summary>The provider has no balance endpoint a plain API key can read.</summary>
    None,
    /// <summary>OpenRouter: the key's own limit, or the account's credits when the key has none.</summary>
    OpenRouter,
    /// <summary>DeepSeek: <c>GET /user/balance</c>.</summary>
    DeepSeek,
    /// <summary>
    /// OpenAI: no balance, but the organization's spend this month from
    /// <c>GET /v1/organization/costs</c>, which takes an admin key rather than the inference key.
    /// </summary>
    OpenAi,
    /// <summary>Deepgram: <c>GET /v1/projects/{id}/balances</c> of the key's project.</summary>
    Deepgram,
    /// <summary>ElevenLabs: <c>GET /v1/user/subscription</c>, in credits rather than money.</summary>
    ElevenLabs,
}
