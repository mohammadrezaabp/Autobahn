namespace Autobahn.Persistence.Entities;
/// <summary>An operator's settings for one paid provider. A provider with no row has the defaults.</summary>
public sealed class ProviderSettingsEntity
{
    public required string Provider { get; set; }
    public decimal? LowBalanceUsd { get; set; }
    public bool NewModelsStartStopped { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
