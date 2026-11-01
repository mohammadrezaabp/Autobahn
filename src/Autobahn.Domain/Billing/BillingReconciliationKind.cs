namespace Autobahn.Domain.Billing;
/// <summary>How a bucket's ledger and rollup disagree.</summary>
public enum BillingReconciliationKind
{
    /// <summary>
    /// Billing events exist for the bucket but no rollup row does — spend that is recorded but
    /// invisible to every dashboard, budget check and daily summary, all of which read rollups.
    /// </summary>
    MissingFromRollups,
    /// <summary>
    /// A rollup row exists with no billing events behind it. Either the events were pruned by
    /// retention (check the window before treating this as a defect) or the rollup was written twice.
    /// </summary>
    MissingFromEvents,
    /// <summary>Both sides exist and their totals differ.</summary>
    TotalsDiffer,
}
