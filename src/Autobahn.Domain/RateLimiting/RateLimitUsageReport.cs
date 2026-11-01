namespace Autobahn.Domain.RateLimiting;
/// <summary>
/// The usage report served from <c>GET /admin/api/rate-limits/usage</c>: what each caller sent, what
/// was refused, and how close everyone is to their configured limit.
/// </summary>
/// <param name = "WindowMinutes">The look-back the counts cover.</param>
/// <param name = "GeneratedUtc">When the report was built.</param>
/// <param name = "Totals">Gateway-wide roll-up of the same window.</param>
/// <param name = "ByTenantModel">
/// The per-user-per-model grid: one row per (tenant, model) pair. This is the section that answers
/// "how does each user use each model".
/// </param>
/// <param name = "ByTenant">Per-user load, summed over every model.</param>
/// <param name = "ByModel">Per-model load, summed over every user.</param>
/// <param name = "ByApiKey">Per-credential load, for finding the one key inside a tenant that is misbehaving.</param>
/// <param name = "Violations">Where limits are actually being hit, most-hit first.</param>
/// <param name = "Adaptive">What the load-aware governor is currently doing, and why.</param>
/// <param name = "Store">Partition-table occupancy, so an operator can see the ceiling approaching.</param>
public sealed record RateLimitUsageReport(int WindowMinutes, DateTimeOffset GeneratedUtc, RateLimitUsageTotals Totals, IReadOnlyList<RateLimitUsageRow> ByTenantModel, IReadOnlyList<RateLimitUsageRow> ByTenant, IReadOnlyList<RateLimitUsageRow> ByModel, IReadOnlyList<RateLimitUsageRow> ByApiKey, IReadOnlyList<RateLimitViolationRow> Violations, AdaptiveRateLimitReport Adaptive, RateLimitStoreReport Store)
{
    /// <summary>
    /// Activity per configured limit, busiest first. Counted at the control that was actually
    /// evaluated — never derived from the per-subject rows above.
    /// </summary>
    public IReadOnlyList<RateLimitLimitUsageRow> Limits { get; init; } = [];
    /// <summary>
    /// The two protective scopes. Always both rows, because their counters are reserved and cannot
    /// be crowded out: a zero here means zero.
    /// </summary>
    public IReadOnlyList<RateLimitProtectiveUsageRow> Protective { get; init; } = [];
    /// <summary>Whether the counters behind this report are complete.</summary>
    public RateLimitTrackerReport Tracker { get; init; } = RateLimitTrackerReport.Empty;
}
