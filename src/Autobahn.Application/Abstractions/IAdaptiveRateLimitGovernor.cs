using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;
/// <summary>
/// The load-aware half of enforcement: watches how saturated each model is and how hard callers are
/// retrying, and adjusts enforcement inside limits the operator set.
/// </summary>
/// <remarks>
/// <para>Two levers, deliberately narrow ones.</para>
///
/// <para><b>Model factor</b> scales the per-model rules down when a model is saturated, so pressure
/// is shed across everyone using that model instead of landing on whoever happens to arrive when the
/// queue fills. It moves by AIMD — cut sharply on pressure, recover in small steps — which is the
/// same control law TCP uses and for the same reason: it converges instead of oscillating. It is
/// bounded below by a configured floor and above by exactly 1.0, so adaptation can only ever enforce
/// <em>more</em> strictly than the configured tier. There is no path by which a model's limit rises
/// above what an operator configured.</para>
///
/// <para><b>Partition backoff</b> lengthens <c>Retry-After</c> for a caller that keeps being
/// refused. A client in a retry storm is the single most expensive kind of rejected traffic — it
/// pays the full admission cost over and over — and telling it to wait longer is the only response
/// that actually reduces load. It escalates only while a partition is being refused and resets the
/// moment one of its requests is admitted, so a bursty-but-legitimate client is never held down.</para>
///
/// <para>Neither lever blocks anything outright, and both are visible: the effective factor is on
/// every response as a header and in the admin report, and <see cref = "Snapshot"/> explains why each
/// model is where it is.</para>
/// </remarks>
public interface IAdaptiveRateLimitGovernor
{
    /// <summary>False when adaptation is switched off; callers then skip every other member.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// What to multiply a model-scoped rule's configured rate by right now, in
    /// <c>[floor, 1.0]</c>. Exactly 1.0 for a model under no pressure, and for every model while
    /// adaptation is disabled.
    /// </summary>
    double GetModelFactor(string modelId);
    /// <summary>
    /// The <c>Retry-After</c> to send this partition, given what the bucket said and how persistently
    /// this partition has been refused. Never below <paramref name = "baseRetryAfterSeconds"/>, never
    /// above the configured ceiling, and jittered so a crowd refused together does not return
    /// together.
    /// </summary>
    int GetRetryAfterSeconds(string partitionKey, int baseRetryAfterSeconds, DateTimeOffset now);
    /// <summary>
    /// Reports an admission decision. Admissions clear a partition's backoff; rejections escalate it.
    /// Cheap enough for the hot path — one dictionary lookup and an interlocked update.
    /// </summary>
    void RecordOutcome(string partitionKey, bool admitted, DateTimeOffset now);
    /// <summary>
    /// Recomputes model factors from the current load signals. Driven by the maintenance timer, never
    /// from a request, so the cost of reading every model's state stays off the request path.
    /// </summary>
    void Evaluate(DateTimeOffset now);
    /// <summary>What the governor is doing and why, for the admin report and the operator console.</summary>
    AdaptiveRateLimitSnapshot Snapshot();
}
