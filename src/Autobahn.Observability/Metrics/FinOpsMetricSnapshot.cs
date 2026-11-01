using System.Diagnostics.Metrics;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models.Overview;

namespace Autobahn.Observability.Metrics;
public sealed class FinOpsMetricSnapshot
{
    public static FinOpsMetricSnapshot Absent { get; } = new();
    public bool HasFinOps { get; init; }
    public bool HasPolicy { get; init; }
    public bool HasTenants { get; init; }
    public double TodayCost { get; init; }
    public double MonthToDateCost { get; init; }
    public int UnpricedModels { get; init; }
    public double AnonymousRequestShare { get; init; }
    public int ActiveKeys { get; init; }
    public int ExpiringKeys { get; init; }
    public int IdleKeys { get; init; }
    public IReadOnlyList<BudgetSample> Budgets { get; init; } = [];
    public IReadOnlyList<QuotaSample> Quotas { get; init; } = [];
    public IReadOnlyList<TenantCostSample> TenantCosts { get; init; } = [];
    public IReadOnlyList<KeyCostSample> Keys { get; init; } = [];
}
