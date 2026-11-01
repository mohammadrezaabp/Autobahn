namespace Autobahn.Domain.Models.Overview;
/// <summary>The Overview's FinOps card: today / month-to-date / projected spend and the data-quality signals behind them.</summary>
public sealed record FinOpsOverview
{
    public DateTimeOffset BuiltAtUtc { get; init; }
    public string Currency { get; init; } = "USD";
    public decimal TodayCost { get; init; }
    public decimal YesterdayCost { get; init; }
    public decimal MonthToDateCost { get; init; }
    public decimal ProjectedMonthlyCost { get; init; }
    public decimal AverageDailyCost { get; init; }
    public long TodayRequests { get; init; }
    public long TodayPromptTokens { get; init; }
    public long TodayCompletionTokens { get; init; }
    public long MonthToDateRequests { get; init; }
    /// <summary>Registered models with no active rate card — their spend is recorded as zero.</summary>
    public IReadOnlyList<string> UnpricedModelIds { get; init; } = [];
    public int PricedModelCount { get; init; }
    public int RegisteredModelCount { get; init; }
    public IReadOnlyList<CostBreakdownRow> TopModelsMonthToDate { get; init; } = [];
    public IReadOnlyList<CostBreakdownRow> TopCostCentersMonthToDate { get; init; } = [];
    public ReconciliationStatus? Reconciliation { get; init; }
    public IReadOnlyList<BudgetStatus> Budgets { get; init; } = [];
}
