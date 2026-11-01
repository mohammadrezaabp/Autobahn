using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

public interface IModelRegistryWriter
{
    Task<RegistryMutationResult> AddModelAsync(ModelConfig model, CancellationToken cancellationToken = default);

    Task<RegistryMutationResult> UpdateModelAsync(string id, ModelConfig model, CancellationToken cancellationToken = default);

    Task<RegistryMutationResult> RemoveModelAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves one route between <c>serving</c> and <c>stopped</c> (see <see cref="ModelRouteStates"/>)
    /// without touching anything else about it.
    /// </summary>
    /// <remarks>
    /// A dedicated mutation rather than a full update: the admin UI's edit path sends the whole
    /// model, so routing a stop through it would make taking a model out of service race with — and
    /// silently overwrite — a concurrent edit of its url, aliases or credential. This reads, flips
    /// and writes the one field under the same gate and route-version check as every other write.
    /// </remarks>
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

    /// <summary>
    /// Replaces the full registry. An empty list is rejected and leaves the registry unchanged.
    /// </summary>
    Task<RegistryMutationResult> ReplaceAllAsync(
        IReadOnlyList<ModelConfig> models,
        CancellationToken cancellationToken = default);
}
