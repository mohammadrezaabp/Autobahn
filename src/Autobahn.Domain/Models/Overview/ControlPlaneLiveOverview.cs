namespace Autobahn.Domain.Models.Overview;
/// <summary>Cheap process facts, refreshed with every summary.</summary>
public sealed record ControlPlaneLiveOverview
{
    public long WorkingSetBytes { get; init; }
    public long GcHeapBytes { get; init; }
    public long GcCommittedBytes { get; init; }
    public int Gen2Collections { get; init; }
    public double GcPauseTimePercent { get; init; }
    public long ThreadPoolPendingWorkItems { get; init; }
    public int ThreadPoolThreads { get; init; }
    public int ProcessorCount { get; init; }
    public double? CpuPercent { get; init; }
    public DateTimeOffset? ConfigLastReloadUtc { get; init; }
    public bool ConfigHotReloadEnabled { get; init; }
    public int ModelCount { get; init; }
}
