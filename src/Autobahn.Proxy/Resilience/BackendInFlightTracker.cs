using System.Collections.Concurrent;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;

namespace Autobahn.Proxy.Resilience;

/// <summary>
/// Counts the requests being forwarded to each backend, from the moment one is chosen until its
/// response has been written or abandoned.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="BulkheadRegistry"/> on purpose. The bulkhead's ceiling and queue are
/// per model, so a request waiting for a slot is not tied to one backend; this is the per-backend
/// count taken after that wait, and it limits nothing.
/// </remarks>
public sealed class BackendInFlightTracker : IBackendInFlightSource
{
    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.OrdinalIgnoreCase);

    public int GetInFlight(string modelId, string backendName) =>
        _counters.TryGetValue(ModelBackends.Key(modelId, backendName), out var counter)
            ? Math.Max(0, Volatile.Read(ref counter.Value))
            : 0;

    /// <summary>Counts one request against the backend until the returned lease is disposed.</summary>
    public IDisposable Enter(string modelId, string backendName)
    {
        var counter = _counters.GetOrAdd(ModelBackends.Key(modelId, backendName), static _ => new Counter());
        Interlocked.Increment(ref counter.Value);
        return new Lease(counter);
    }

    private sealed class Counter
    {
        public int Value;
    }

    private sealed class Lease(Counter counter) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Interlocked.Decrement(ref counter.Value);
            }
        }
    }
}
