namespace Autobahn.Proxy.Resilience;

/// <summary>
/// Guarantees that every request admitted by <see cref="ModelCircuitBreakerRegistry.TryEnter"/>
/// reports an outcome to the breaker.
/// </summary>
/// <remarks>
/// Admission consumes the breaker's single half-open probe permit, and that permit is only restored
/// by recording an outcome. Any path that returns or throws between admission and forwarding without
/// reporting one leaks the permit, leaving the breaker in HalfOpen with no probe available — which
/// rejects every subsequent request for that model until the process restarts.
///
/// Disposing without an explicit outcome abandons the probe: the permit is released, but no success
/// or failure is attributed to the backend. That is the correct default, because the paths that hit
/// it (client aborts, gateway-side rejections, configuration errors) carry no evidence either way.
/// </remarks>
public sealed class CircuitBreakerProbeLease : IDisposable
{
    private readonly ModelCircuitBreakerRegistry _registry;
    private readonly string _modelId;
    private readonly string? _caller;
    private bool _outcomeRecorded;

    /// <param name="caller">
    /// Who sent the request, so the breaker can require failures from more than one caller before it
    /// opens. Null leaves the outcome unattributed.
    /// </param>
    public CircuitBreakerProbeLease(ModelCircuitBreakerRegistry registry, string modelId, string? caller = null)
    {
        _registry = registry;
        _modelId = modelId;
        _caller = caller;
    }

    /// <summary>Records a healthy backend response, closing the breaker.</summary>
    public void RecordSuccess()
    {
        if (_outcomeRecorded)
        {
            return;
        }

        _outcomeRecorded = true;
        _registry.RecordSuccess(_modelId, _caller);
    }

    /// <summary>
    /// Records evidence that the backend itself is unhealthy. Do not call this for gateway-side
    /// rejections or client aborts — let <see cref="Dispose"/> abandon the probe instead.
    /// </summary>
    public void RecordFailure()
    {
        if (_outcomeRecorded)
        {
            return;
        }

        _outcomeRecorded = true;
        _registry.RecordFailure(_modelId, _caller);
    }

    public void Dispose()
    {
        if (_outcomeRecorded)
        {
            return;
        }

        _outcomeRecorded = true;
        _registry.RecordAbandoned(_modelId);
    }
}
