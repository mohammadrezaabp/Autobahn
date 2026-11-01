using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;

namespace Autobahn.Integration.Tests.Support;

internal sealed class AlwaysUnhealthyBackendHealthStore : IBackendHealthStore
{
    public bool IsBackendHealthy(string modelId) => false;

    public BackendHealth? GetHealth(string modelId) => null;

    public IReadOnlyDictionary<string, BackendHealth> GetAllHealth() =>
        new Dictionary<string, BackendHealth>();

    public void SetHealth(BackendHealth health)
    {
        // No-op: this test store always reports unhealthy.
    }
}
