namespace Autobahn.Domain.Models.Overview;
public sealed record WindowModelStats
{
    public required string ModelId { get; init; }
    public long Requests { get; init; }
    public long Errors { get; init; }
    public double ErrorRate { get; init; }
    public double LatencyP95Ms { get; init; }
    public double? TtftP95Ms { get; init; }
    public long PromptTokens { get; init; }
    public long CompletionTokens { get; init; }
    public decimal PricedCost { get; init; }
}
