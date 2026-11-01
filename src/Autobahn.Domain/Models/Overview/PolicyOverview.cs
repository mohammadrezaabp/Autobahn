namespace Autobahn.Domain.Models.Overview;
/// <summary>Database-backed policy state: quota consumption and budgets per tenant.</summary>
public sealed record PolicyOverview
{
    public DateTimeOffset BuiltAtUtc { get; init; }
    public IReadOnlyList<QuotaStatus> Quotas { get; init; } = [];
    public IReadOnlyList<BudgetStatus> BudgetsNearLimit { get; init; } = [];
    public IReadOnlyList<CountRow> GrantDenials { get; init; } = [];
    public IReadOnlyList<CountRow> UnknownModels { get; init; } = [];
}
