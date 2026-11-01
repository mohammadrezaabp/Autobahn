namespace Autobahn.Domain.Models.Overview;
public sealed record OverviewSeriesPoint
{
    public DateTimeOffset T { get; init; }
    public long Requests { get; init; }
    public long Errors { get; init; }
    public double LatencyP95Ms { get; init; }
    public double? TtftP95Ms { get; init; }
    /// <summary>Peak in-flight requests sampled during the minute.</summary>
    public int InFlight { get; init; }
    public long Tokens { get; init; }
    public decimal Cost { get; init; }
}
