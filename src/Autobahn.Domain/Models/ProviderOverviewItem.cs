namespace Autobahn.Domain.Models;
/// <summary>One paid provider as the Providers page shows it.</summary>
public sealed class ProviderOverviewItem
{
    public required string Provider { get; init; }
    public required string DisplayName { get; init; }
    public required string UpstreamBaseUrl { get; init; }
    /// <summary>The environment variable the company key is read from when none is stored.</summary>
    public required string EnvVar { get; init; }
    public bool KeyConfigured { get; init; }
    /// <summary><c>stored</c>, <c>environment</c> or <c>none</c>.</summary>
    public required string KeySource { get; init; }
    /// <summary>False for a provider whose API the gateway does not forward yet (speech).</summary>
    public bool Forwardable { get; init; }
    public bool SupportsCatalog { get; init; }
    /// <summary>Whether the provider reports each request's charge, so cost is not computed from a price.</summary>
    public bool ReportsCost { get; init; }
    public bool SupportsBalance { get; init; }
    /// <summary>
    /// The environment variable the balance is read with, when it is not the company key
    /// (OpenAI's costs take an admin key); null when the company key reads it.
    /// </summary>
    public string? BalanceEnvVar { get; init; }
    /// <summary>Whether the key the balance is read with is set.</summary>
    public bool BalanceKeyConfigured { get; init; }
    public ProviderBalanceDto? Balance { get; init; }
    public decimal? LowBalanceUsd { get; init; }
    public bool NewModelsStartStopped { get; init; }
    public int ModelCount { get; init; }
    public int OpenTenantCount { get; init; }
    public int OpenKeyCount { get; init; }
    public decimal SpentThisMonthUsd { get; init; }
    public int RequestsThisMonth { get; init; }
    /// <summary>The hours the provider bills more for; null when it charges the same at every hour.</summary>
    public ProviderPeakPricingDto? PeakPricing { get; init; }
}
