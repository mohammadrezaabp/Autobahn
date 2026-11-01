using System.Text.Json.Serialization;

namespace Autobahn.Domain.Configuration;
/// <summary>
/// Bounds and gains for load-aware enforcement. Every one of them is a limit on how far the governor
/// may move, never on how far a configured tier may be exceeded — the factor is capped at 1.0 in the
/// governor and again where it is applied.
/// </summary>
public sealed class AdaptiveRateLimitOptions
{
    /// <summary>
    /// Master switch for adaptation. Off by default: a gateway should enforce exactly what its
    /// operator configured until that operator asks for something cleverer.
    /// </summary>
    public bool Enabled { get; set; }
    /// <summary>
    /// The furthest a model's rules may be scaled down. At 0.25 a saturated model still admits a
    /// quarter of its configured rate, so adaptation degrades service rather than causing an outage.
    /// </summary>
    public double MinFactor { get; set; } = 0.25;
    /// <summary>Saturation at or above which a model's factor is cut. Below <see cref = "LowWatermark"/> it recovers; between the two it holds, which is what stops it oscillating.</summary>
    public double HighWatermark { get; set; } = 0.85;
    /// <summary>Saturation at or below which a model's factor recovers towards 1.0.</summary>
    public double LowWatermark { get; set; } = 0.5;
    /// <summary>Multiplicative cut applied per evaluation while a model is over the high watermark.</summary>
    public double DecreaseFactor { get; set; } = 0.8;
    /// <summary>Additive recovery applied per evaluation while a model is under the low watermark.</summary>
    public double IncreaseStep { get; set; } = 0.05;
    /// <summary>
    /// Consecutive refusals a partition must accumulate before its <c>Retry-After</c> starts
    /// escalating. Set above a handful so an ordinary burst that briefly overruns its tier is told to
    /// wait the usual second.
    /// </summary>
    public int BackoffAfterConsecutiveRejections { get; set; } = 5;
    /// <summary>Multiplier applied per rejection past the threshold, compounding up to the ceiling.</summary>
    public double BackoffGrowthFactor { get; set; } = 1.6;
    /// <summary>
    /// Longest <c>Retry-After</c> the gateway will ever send. Clients sleep for this header, so an
    /// unbounded value is indistinguishable from a hang.
    /// </summary>
    public int MaxRetryAfterSeconds { get; set; } = 60;
    /// <summary>
    /// Share of the computed <c>Retry-After</c> spread randomly, in <c>[0, 1)</c>. Without it every
    /// client refused in the same second returns in the same later second, and the gateway
    /// synchronises its own thundering herd.
    /// </summary>
    public double RetryAfterJitter { get; set; } = 0.2;
}
