namespace Autobahn.Application.Abstractions;
public sealed record BulkheadModelState(string ModelId, int InFlight, int Queued, int MaxConcurrent, int MaxQueued);
