using Autobahn.Domain.Billing;

namespace Autobahn.Domain.Models;
public sealed class UsageReportResponse
{
    public required IReadOnlyList<DailyUsageRollupRecord> Rollups { get; init; }
    public required UsageReportSummary Summary { get; init; }
    /// <summary>ISO currency the costs are labelled in (the gateway's default billing currency).</summary>
    public string Currency { get; init; } = "USD";
    /// <summary>
    /// <c>rollups</c> when read from the daily rollup table, <c>events</c> when aggregated from the
    /// ledger (per-key reports).
    /// </summary>
    public string Source { get; init; } = UsageReportSource.Rollups;
    /// <summary>
    /// Models present in <see cref = "Rollups"/> that currently have no rate card. Their spend is
    /// recorded as zero, which is otherwise indistinguishable from genuinely free usage.
    /// </summary>
    public IReadOnlyList<string> UnpricedModelIds { get; init; } = [];
}
