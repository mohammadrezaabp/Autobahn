namespace Autobahn.Domain.Models.Overview;
public sealed record BudgetStatus
{
    public Guid BudgetId { get; init; }
    public Guid TenantId { get; init; }
    public string? TenantSlug { get; init; }
    public required string Name { get; init; }
    public string Currency { get; init; } = "USD";
    public decimal Limit { get; init; }
    public decimal Spent { get; init; }
    /// <summary>Cost reserved by requests still in flight.</summary>
    public decimal Outstanding { get; init; }
    /// <summary>(Spent + Outstanding) ÷ Limit.</summary>
    public double Ratio { get; init; }
    public double WarningRatio { get; init; }
    public bool HardStopEnabled { get; init; }
    public DateOnly PeriodStart { get; init; }
    /// <summary>Day the budget is projected to be exhausted at the trailing average daily spend; null when not before period end.</summary>
    public DateOnly? ProjectedBreachDate { get; init; }
}
