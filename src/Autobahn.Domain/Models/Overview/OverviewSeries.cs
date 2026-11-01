namespace Autobahn.Domain.Models.Overview;
/// <summary>
/// A short server-side time series (one point per minute, oldest first) for the Overview sparklines,
/// so every operator sees the same trend and it survives a page reload.
/// </summary>
public sealed record OverviewSeries
{
    public DateTimeOffset StartUtc { get; init; }
    public int StepSeconds { get; init; } = 60;
    public IReadOnlyList<OverviewSeriesPoint> Points { get; init; } = [];
}
