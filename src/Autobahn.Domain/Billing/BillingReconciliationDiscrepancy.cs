namespace Autobahn.Domain.Billing;
/// <summary>One bucket whose stored rollup disagrees with the billing events behind it.</summary>
public sealed record BillingReconciliationDiscrepancy(BillingReconciliationKind Kind, DailyUsageRollupKey Key, BillingReconciliationTotals Events, BillingReconciliationTotals Rollup)
{
    /// <summary>Rollup minus events. Negative means the rollups under-report actual spend.</summary>
    public decimal CostDelta => Rollup.TotalCost - Events.TotalCost;
    public long TokenDelta => Rollup.TotalTokens - Events.TotalTokens;
    public int RequestCountDelta => Rollup.RequestCount - Events.RequestCount;
}
