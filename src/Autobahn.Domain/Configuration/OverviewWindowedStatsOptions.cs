namespace Autobahn.Domain.Configuration;
public sealed class OverviewWindowedStatsOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Upper bound on models with their own 24-hour ring. Each ring costs roughly half a megabyte;
    /// models past the cap still count toward the gateway-wide windows.
    /// </summary>
    public int MaxTrackedModels { get; set; } = 64;
}
