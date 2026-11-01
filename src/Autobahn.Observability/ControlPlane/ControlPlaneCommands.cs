using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;

namespace Autobahn.Observability.ControlPlane;

public sealed class ControlPlaneCommands(
    IConfigReload configReload,
    IModelRegistry registry,
    IBackendHealthStore healthStore,
    IAdminSummaryReader summaryReader,
    IRecentRequestStore recentRequestStore,
    IModelRegistryWriter registryWriter,
    IBackendInFlightSource? inFlight = null) : IControlPlaneCommands
{
    public Task<ConfigReloadResult> ReloadConfigAsync(CancellationToken cancellationToken = default) =>
        configReload.ReloadAsync(cancellationToken);

    public ConfigStatusResponse GetConfigStatus() => configReload.GetStatus();

    public AdminSummarySnapshot GetSummary() => summaryReader.GetSnapshot();

    public IReadOnlyList<BackendAdminDto> ListBackends()
    {
        var rows = new List<BackendAdminDto>();
        foreach (var m in registry.GetAllModels())
        {
            foreach (var b in m.GetBackends())
            {
                var serving = m.IsServing() && b.IsServing();
                var health = healthStore.GetHealth(m.Id, b.Name);
                rows.Add(new BackendAdminDto
                {
                    ModelId = m.Id,
                    Backend = b.Name,
                    Url = b.Url,
                    MaxContextLength = b.MaxContextLength,
                    Priority = BackendPriorities.Normalize(b.Priority),
                    UpstreamModel = b.UpstreamModel,
                    Provider = ModelProviders.ResolveBackend(m, b),
                    InFlight = inFlight?.GetInFlight(m.Id, b.Name) ?? 0,
                    Labels = b.Labels,
                    // A stopped backend is not probed, so its health is stale by definition; the
                    // state is what explains the missing probe.
                    IsHealthy = serving && healthStore.IsBackendHealthy(m.Id, b.Name),
                    State = serving ? ModelRouteStates.Serving : ModelRouteStates.Stopped,
                    Alias = m.Aliases.Count > 0 ? m.Aliases[0] : null,
                    StatusCode = health?.StatusCode,
                    Error = health?.Error,
                    LastCheckedUtc = health?.LastCheckedUtc,
                    LastTransitionUtc = health?.LastTransitionUtc,
                });
            }
        }

        return rows;
    }

    public IReadOnlyList<ModelConfig> ListModels() => registry.GetAllModels();

    public IReadOnlyList<RecentRequestEntry> ListRecentRequests(int limit) =>
        recentRequestStore.GetRecent(limit);

    public Task<RegistryMutationResult> AddModelAsync(ModelConfig model, CancellationToken cancellationToken = default) =>
        registryWriter.AddModelAsync(model, cancellationToken);

    public Task<RegistryMutationResult> UpdateModelAsync(string id, ModelConfig model, CancellationToken cancellationToken = default) =>
        registryWriter.UpdateModelAsync(id, model, cancellationToken);

    public Task<RegistryMutationResult> RemoveModelAsync(string id, CancellationToken cancellationToken = default) =>
        registryWriter.RemoveModelAsync(id, cancellationToken);

    public Task<RegistryMutationResult> SetModelStateAsync(string id, string state, CancellationToken cancellationToken = default) =>
        registryWriter.SetModelStateAsync(id, state, cancellationToken);

    public Task<RegistryMutationResult> AddBackendAsync(string id, ModelBackendConfig backend, CancellationToken cancellationToken = default) =>
        registryWriter.AddBackendAsync(id, backend, cancellationToken);

    public Task<RegistryMutationResult> UpdateBackendAsync(string id, string name, ModelBackendConfig backend, CancellationToken cancellationToken = default) =>
        registryWriter.UpdateBackendAsync(id, name, backend, cancellationToken);

    public Task<RegistryMutationResult> RemoveBackendAsync(string id, string name, CancellationToken cancellationToken = default) =>
        registryWriter.RemoveBackendAsync(id, name, cancellationToken);

    public Task<RegistryMutationResult> SetBackendStateAsync(string id, string name, string state, CancellationToken cancellationToken = default) =>
        registryWriter.SetBackendStateAsync(id, name, state, cancellationToken);
}
