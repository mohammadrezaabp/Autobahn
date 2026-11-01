namespace Autobahn.Domain.Billing;
/// <summary>
/// The result of comparing the <c>billing_events</c> ledger against the <c>daily_usage_rollups</c>
/// derived from it.
/// </summary>
/// <remarks>
/// The ledger is the source of truth: it is the append-only, idempotent record written per request.
/// Rollups are a derived aggregate, and everything an operator actually looks at — the admin usage
/// pages, budget enforcement, the daily webhook — reads the aggregate, not the ledger. So any defect
/// between the two is invisible by construction: the numbers stay plausible and nothing errors. This
/// report is what makes that class of failure observable.
/// </remarks>
public sealed record BillingReconciliationReport(DateOnly FromDate, DateOnly ToDate, int BucketsCompared, BillingReconciliationTotals EventTotals, BillingReconciliationTotals RollupTotals, IReadOnlyList<BillingReconciliationDiscrepancy> Discrepancies)
{
    public static BillingReconciliationReport Empty(DateOnly fromDate, DateOnly toDate) => new(fromDate, toDate, 0, BillingReconciliationTotals.Zero, BillingReconciliationTotals.Zero, []);
    public bool IsBalanced => Discrepancies.Count == 0;
    /// <summary>Rollup cost minus ledger cost across the window. Signed, so offsetting drift cancels.</summary>
    public decimal NetCostDrift => RollupTotals.TotalCost - EventTotals.TotalCost;
    /// <summary>
    /// Sum of the absolute per-bucket cost deltas. Unlike <see cref = "NetCostDrift"/> this does not
    /// let an over-count in one bucket hide an under-count in another, so it is the figure to alert
    /// on.
    /// </summary>
    public decimal AbsoluteCostDrift => Discrepancies.Sum(d => Math.Abs(d.CostDelta));
}
