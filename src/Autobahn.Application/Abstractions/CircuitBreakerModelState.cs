namespace Autobahn.Application.Abstractions;
/// <param name = "OpenedAt">When the breaker last tripped; null while closed.</param>
/// <param name = "FailuresInWindow">Failures inside the sampling window (a closed breaker creeping toward its threshold).</param>
/// <param name = "OutcomesInWindow">All outcomes inside the sampling window.</param>
/// <param name = "LastTransitionUtc">When the state last changed; null if it never has.</param>
/// <param name = "BackendName">Which of the model's backends the breaker guards.</param>
public sealed record CircuitBreakerModelState(string ModelId, int State, DateTimeOffset? OpenedAt = null, int FailuresInWindow = 0, int OutcomesInWindow = 0, DateTimeOffset? LastTransitionUtc = null, string BackendName = Autobahn.Domain.Models.ModelBackends.DefaultName);
