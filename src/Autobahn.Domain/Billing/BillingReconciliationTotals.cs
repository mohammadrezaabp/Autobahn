namespace Autobahn.Domain.Billing;
/// <summary>Aggregate usage totals for one reconciliation bucket, or for a whole report.</summary>
public readonly record struct BillingReconciliationTotals(long PromptTokens, long CompletionTokens, decimal TotalCost, int RequestCount)
{
    public static readonly BillingReconciliationTotals Zero = new(0, 0, 0m, 0);
    public long TotalTokens => PromptTokens + CompletionTokens;

    public static BillingReconciliationTotals operator +(BillingReconciliationTotals left, BillingReconciliationTotals right) => new(left.PromptTokens + right.PromptTokens, left.CompletionTokens + right.CompletionTokens, left.TotalCost + right.TotalCost, left.RequestCount + right.RequestCount);
}
