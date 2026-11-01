namespace Autobahn.Application.Providers;
/// <summary>When the provider's figure and the gateway's are far enough apart to flag.</summary>
/// <param name = "Percent">Difference, as a share of the larger of the two, from which to flag.</param>
/// <param name = "MinimumUsd">Difference below which nothing is flagged, so a quiet day does not alarm.</param>
public sealed record ProviderDriftThreshold(decimal Percent, decimal MinimumUsd)
{
    public static readonly ProviderDriftThreshold Default = new(10m, 1m);
}
