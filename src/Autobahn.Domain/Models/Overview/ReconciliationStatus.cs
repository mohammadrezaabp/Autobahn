namespace Autobahn.Domain.Models.Overview;
public sealed record ReconciliationStatus
{
    public bool Enabled { get; init; }
    public DateTimeOffset? LastRunUtc { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public int BucketsCompared { get; init; }
    public int DiscrepancyCount { get; init; }
    public decimal AbsoluteCostDrift { get; init; }
    public bool IsBalanced => DiscrepancyCount == 0;
}
