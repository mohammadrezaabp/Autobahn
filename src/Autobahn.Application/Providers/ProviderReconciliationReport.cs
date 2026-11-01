namespace Autobahn.Application.Providers;
/// <summary>
/// The provider's spend, read from the fall of its balance, beside the gateway's own charges for
/// the same stretches of time.
/// </summary>
public sealed class ProviderReconciliationReport
{
    public required string Provider { get; init; }
    public required string Currency { get; init; }
    public DateTimeOffset WindowFrom { get; init; }
    public DateTimeOffset WindowTo { get; init; }
    /// <summary>First balance reading the comparison starts from; null with fewer than two.</summary>
    public DateTimeOffset? CoveredFrom { get; init; }
    /// <summary>Last balance reading the comparison runs to.</summary>
    public DateTimeOffset? CoveredTo { get; init; }
    public int Readings { get; init; }
    /// <summary>Stretches between two readings in which the balance rose; left out on both sides.</summary>
    public int TopUps { get; init; }
    /// <summary>What the balance fell by: the whole account's spend, keys outside the gateway included.</summary>
    public decimal ProviderSpendUsd { get; init; }
    /// <summary>What the gateway charged for requests it sent there over the same stretches.</summary>
    public decimal GatewaySpendUsd { get; init; }
    /// <summary>Provider minus gateway: positive when the provider took more than the gateway counted.</summary>
    public decimal DifferenceUsd { get; init; }
    /// <summary>The difference as a percentage of the larger figure; null when both are zero.</summary>
    public decimal? DifferencePercent { get; init; }
    public bool Drifting { get; init; }
    public required ProviderDriftThreshold Threshold { get; init; }
    /// <summary>Why no comparison could be made (too few readings, or a balance in another currency); null when one was.</summary>
    public string? Unavailable { get; init; }
    public required IReadOnlyList<ProviderReconciliationDay> Daily { get; init; }
}
