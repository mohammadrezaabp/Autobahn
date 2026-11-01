using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

public interface IBackendHealthStore
{
    /// <summary>
    /// Whether the model can be forwarded to: true when at least one of its probed backends is
    /// healthy, and when none has been probed yet unless strict mode says otherwise.
    /// </summary>
    bool IsBackendHealthy(string modelId);

    /// <summary>The same question for one backend of the model.</summary>
    bool IsBackendHealthy(string modelId, string backendName) => IsBackendHealthy(modelId);

    /// <summary>
    /// The verdict that stands for the model: a healthy backend's when there is one, otherwise a
    /// failing one's. Null until a backend of the model has been probed.
    /// </summary>
    BackendHealth? GetHealth(string modelId);

    BackendHealth? GetHealth(string modelId, string backendName) => GetHealth(modelId);

    IReadOnlyDictionary<string, BackendHealth> GetAllHealth();

    void SetHealth(BackendHealth health);
}
