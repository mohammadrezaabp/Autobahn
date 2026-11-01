namespace Autobahn.Domain.Configuration;
/// <summary>Settings for the admin Overview page (<c>Gateway:Overview</c>).</summary>
public sealed class OverviewOptions
{
    public OverviewWindowedStatsOptions WindowedStats { get; set; } = new();
    /// <summary>
    /// How long a database-backed Overview section (FinOps, policy, control plane, activity,
    /// tenants) is served from memory before it is rebuilt.
    /// </summary>
    public int SlowSectionTtlSeconds { get; set; } = 15;
    public OverviewAttentionOptions Attention { get; set; } = new();
}
