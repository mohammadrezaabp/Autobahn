using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

public interface IControlPlaneCommands
{
    Task<ConfigReloadResult> ReloadConfigAsync(CancellationToken cancellationToken = default);

    ConfigStatusResponse GetConfigStatus();

    AdminSummarySnapshot GetSummary();

    IReadOnlyList<BackendAdminDto> ListBackends();

    IReadOnlyList<ModelConfig> ListModels();

    IReadOnlyList<RecentRequestEntry> ListRecentRequests(int limit);

    Task<RegistryMutationResult> AddModelAsync(ModelConfig model, CancellationToken cancellationToken = default);

    Task<RegistryMutationResult> UpdateModelAsync(string id, ModelConfig model, CancellationToken cancellationToken = default);

    Task<RegistryMutationResult> RemoveModelAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a model route out of service (<c>stopped</c>) or puts it back (<c>serving</c>).
    /// See <see cref="ModelRouteStates"/>.
    /// </summary>
    Task<RegistryMutationResult> SetModelStateAsync(
        string id,
        string state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds one backend to a model, leaving the model's other backends and fields as they are.
    /// </summary>
    /// <remarks>
    /// The backend mutations are each their own write for the reason <c>SetModelStateAsync</c> is:
    /// a whole-model update carries the whole list, so two callers each adding a backend would
    /// otherwise overwrite one another.
    /// </remarks>
    Task<RegistryMutationResult> AddBackendAsync(
        string id,
        ModelBackendConfig backend,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RegistryMutationResult.Fail("Backends are not supported by this registry.", 501));

    /// <summary>Replaces one backend's address, credential, context length and labels; its name and state stay.</summary>
    Task<RegistryMutationResult> UpdateBackendAsync(
        string id,
        string name,
        ModelBackendConfig backend,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RegistryMutationResult.Fail("Backends are not supported by this registry.", 501));

    /// <summary>Removes one backend. Removing the last leaves the model registered with nothing to forward to.</summary>
    Task<RegistryMutationResult> RemoveBackendAsync(
        string id,
        string name,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RegistryMutationResult.Fail("Backends are not supported by this registry.", 501));

    /// <summary>Moves one backend between <c>serving</c> and <c>stopped</c>.</summary>
    Task<RegistryMutationResult> SetBackendStateAsync(
        string id,
        string name,
        string state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RegistryMutationResult.Fail("Backends are not supported by this registry.", 501));
}
