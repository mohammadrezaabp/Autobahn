using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;

namespace Autobahn.Integration.Tests.Support;

internal sealed class AlwaysHealthyBackendHealthStore : IBackendHealthStore
{
    public bool IsBackendHealthy(string modelId) => true;

    public BackendHealth? GetHealth(string modelId) =>
        new(modelId, "http://test", true, 200, null, DateTimeOffset.UtcNow);

    public IReadOnlyDictionary<string, BackendHealth> GetAllHealth() =>
        new Dictionary<string, BackendHealth>();

    public void SetHealth(BackendHealth health)
    {
        // No-op: this test store always reports healthy.
    }
}
