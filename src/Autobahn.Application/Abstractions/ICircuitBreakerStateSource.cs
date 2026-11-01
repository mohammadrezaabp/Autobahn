namespace Autobahn.Application.Abstractions;
/// <summary>
/// Snapshot of per-model circuit breaker state for Prometheus observable gauges and the admin
/// Overview. State values: 0 = closed, 1 = half_open, 2 = open.
/// </summary>
public interface ICircuitBreakerStateSource
{
    IReadOnlyList<CircuitBreakerModelState> GetStates();
}
