namespace Autobahn.Domain.Models.Overview;
/// <summary>In-memory policy pressure: who is being throttled, by which control, in the last hour and day.</summary>
public sealed record PolicyLiveOverview
{
    public IReadOnlyList<CountRow> RejectionsByReason1h { get; init; } = [];
    public IReadOnlyList<CountRow> RejectionsByReason24h { get; init; } = [];
    public IReadOnlyList<CountRow> RejectionsByTenant1h { get; init; } = [];
    public IReadOnlyList<CountRow> RejectionsByModel1h { get; init; } = [];
    /// <summary>Model names clients asked for that the registry does not know.</summary>
    public IReadOnlyList<CountRow> UnknownModels1h { get; init; } = [];
    public IReadOnlyList<CountRow> GrantDenials1h { get; init; } = [];
    public IReadOnlyList<CountRow> BudgetRejections1h { get; init; } = [];
}
