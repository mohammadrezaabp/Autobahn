namespace Autobahn.Domain.Models;
public sealed class SetProviderSettingsRequest
{
    /// <summary>Remaining credit below which the low-credit alert fires. Null turns the alert off.</summary>
    public decimal? LowBalanceUsd { get; init; }
    public bool NewModelsStartStopped { get; init; }
}
