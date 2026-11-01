namespace Autobahn.Domain.Models;
public sealed class SetProviderAccessRequest
{
    public bool Open { get; init; }
    /// <summary>The monthly cap in USD. Left out, the cap stays as it is.</summary>
    public decimal? MonthlyCapUsd { get; init; }
    /// <summary>True removes the cap; <see cref = "MonthlyCapUsd"/> is then ignored.</summary>
    public bool ClearCap { get; init; }
    public bool ChangesCap => ClearCap || MonthlyCapUsd is not null;
}
