namespace Autobahn.Domain.Models;
public sealed class ProviderSpendReport
{
    public required string Provider { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public decimal TotalUsd { get; init; }
    public int Requests { get; init; }
    public required IReadOnlyList<ProviderSpendDay> Daily { get; init; }
    public required IReadOnlyList<ProviderSpendSlice> ByTenant { get; init; }
    public required IReadOnlyList<ProviderSpendSlice> ByKey { get; init; }
    public required IReadOnlyList<ProviderSpendSlice> ByModel { get; init; }
}
