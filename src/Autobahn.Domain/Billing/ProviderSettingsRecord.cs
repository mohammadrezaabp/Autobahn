namespace Autobahn.Domain.Billing;
/// <summary>What an operator set for one paid provider on the Providers page.</summary>
/// <param name = "LowBalanceUsd">Remaining credit below which the low-credit alert fires; null for no alert.</param>
/// <param name = "NewModelsStartStopped">Whether a model added from the catalogue starts stopped.</param>
public sealed record ProviderSettingsRecord(string Provider, decimal? LowBalanceUsd, bool NewModelsStartStopped);
