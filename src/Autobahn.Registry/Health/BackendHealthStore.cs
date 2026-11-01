using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Domain.Models;

namespace Autobahn.Registry.Health;

public sealed class BackendHealthStore : IBackendHealthStore
{
    private readonly ConcurrentDictionary<string, BackendHealth> _health =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly bool _strictMode;

    public BackendHealthStore(IOptions<GatewayOptions> options)
    {
        _strictMode = options.Value.HealthCheckStrictMode;
    }

    public bool IsBackendHealthy(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        // The model can be forwarded to when any of its probed backends can. Only backends in
        // service are probed and kept here, so a stopped one never speaks for the model.
        var probed = false;
        foreach (var health in _health.Values)
        {
            if (!string.Equals(health.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (health.IsHealthy)
            {
                return true;
            }

            probed = true;
        }

        return !probed && !_strictMode;
    }

    public bool IsBackendHealthy(string modelId, string backendName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        if (_health.TryGetValue(ModelBackends.Key(modelId, backendName), out var health))
        {
            return health.IsHealthy;
        }

        return !_strictMode;
    }

    public BackendHealth? GetHealth(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        BackendHealth? failing = null;
        foreach (var health in _health.Values)
        {
            if (!string.Equals(health.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (health.IsHealthy)
            {
                return health;
            }

            failing ??= health;
        }

        return failing;
    }

    public BackendHealth? GetHealth(string modelId, string backendName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        return _health.TryGetValue(ModelBackends.Key(modelId, backendName), out var health) ? health : null;
    }

    public IReadOnlyDictionary<string, BackendHealth> GetAllHealth() =>
        new Dictionary<string, BackendHealth>(_health, StringComparer.OrdinalIgnoreCase);

    public void SetHealth(BackendHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        // Each sweep overwrites the row wholesale, so the "since when" an operator wants for an
        // unhealthy backend is carried across here: the transition stamp survives as long as the
        // state does not flip again.
        _health.AddOrUpdate(
            health.Key,
            static (_, incoming) => incoming with { LastTransitionUtc = incoming.LastTransitionUtc ?? incoming.LastCheckedUtc },
            static (_, previous, incoming) => incoming with
            {
                LastTransitionUtc = previous.IsHealthy == incoming.IsHealthy
                    ? previous.LastTransitionUtc ?? previous.LastCheckedUtc
                    : incoming.LastCheckedUtc,
            },
            health);
    }

    /// <summary>
    /// Forgets every backend whose key (<see cref="ModelBackends.Key"/>) is not in
    /// <paramref name="keys"/>. Called after each health sweep so backends that were deleted,
    /// renamed or stopped stop showing stale rows in the backends view, stop being answered by
    /// <see cref="IsBackendHealthy(string)"/>, and stop accumulating over add/rename/delete cycles.
    /// </summary>
    public void RetainOnly(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var keep = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in _health.Keys)
        {
            if (!keep.Contains(key))
            {
                _health.TryRemove(key, out _);
            }
        }
    }
}
