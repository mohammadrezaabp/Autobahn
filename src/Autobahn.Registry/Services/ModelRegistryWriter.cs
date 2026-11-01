using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;

namespace Autobahn.Registry.Services;

/// <summary>
/// Applies admin mutations to the model registry: reads the current route set from the database
/// (<see cref="IModelRouteRepository"/>), applies the change, validates the <em>result</em>, persists
/// it, then swaps the in-memory registry so the change is live immediately. Writes are serialized by
/// <see cref="RegistryGate"/> within a process and by the route version across processes. Requires a
/// configured database; DB-less deployments are read-only from models.json.
/// </summary>
/// <remarks>
/// Two ordering rules keep the database and the live registry from diverging, and both were learned
/// the hard way:
/// <list type="bullet">
/// <item>The candidate set is validated before it is persisted. Persisting first and validating
/// during the in-memory swap left rejected models in the database, which then failed to load at the
/// next startup — an empty registry, every request failing, and the next successful write deleting
/// every other route.</item>
/// <item>The current set is read from the database, not from the in-memory registry. Memory is a
/// per-process cache that a second replica, a failed load, or a concurrent write can make stale, and
/// a stale read here rewrites the whole table from that stale state.</item>
/// </list>
/// </remarks>
public sealed class ModelRegistryWriter(
    ModelRegistryService registry,
    RegistryGate gate,
    IServiceScopeFactory scopeFactory,
    IUpstreamSecretStore secretStore,
    ILogger<ModelRegistryWriter> logger) : IModelRegistryWriter
{
    public async Task<RegistryMutationResult> AddModelAsync(
        ModelConfig model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        // A model written with a list of backends may be registered before it has one.
        if (string.IsNullOrWhiteSpace(model.Id) ||
            (model.Backends is null && string.IsNullOrWhiteSpace(model.Url)))
        {
            return RegistryMutationResult.Fail("Model id and url are required.");
        }

        if (!ModelConfigValidation.TryValidate(model, out var validationError))
        {
            return RegistryMutationResult.Fail(validationError!);
        }

        return await MutateAsync(
            current =>
            {
                if (TryFindById(current, model.Id) is not null)
                {
                    return MutationPlan.Rejected($"Model '{model.Id}' already exists.", 409);
                }

                if (FindAliasOwner(current, model.Id) is { } aliasOwner)
                {
                    // Not "already exists": no model carries this id, so the operator would look for
                    // it in the list and not find it. Name the model that actually holds the alias.
                    return MutationPlan.Rejected(
                        $"'{model.Id}' is already an alias of model '{aliasOwner.Id}'. " +
                        "Remove that alias first, or choose a different model id.",
                        409);
                }

                return MutationPlan.Ok([.. current, ModelRegistryPersistence.CloneModel(model)]);
            },
            $"Model '{model.Id.Trim()}' added.",
            "add model",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RegistryMutationResult> UpdateModelAsync(
        string id,
        ModelConfig model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(model);

        string? renamedTo = null;

        var result = await MutateAsync(
            current =>
            {
                var existing = TryFindById(current, id) ?? FindAliasOwner(current, id);
                if (existing is null)
                {
                    return MutationPlan.Rejected($"Model '{id}' was not found.", 404);
                }

                // A blank id in the body means "leave the id alone"; a different one is a rename.
                // Silently pinning the id to the existing one (the old behaviour) reported success
                // for a rename that never happened.
                // Ids are matched case-insensitively everywhere else (routing, grants, rate cards), so
                // differently-cased spelling of the same id addresses the same model rather than
                // renaming it — treating it as a rename would silently re-key its price and credential.
                var targetId = string.IsNullOrWhiteSpace(model.Id) ? existing.Id : model.Id.Trim();
                if (string.Equals(targetId, existing.Id, StringComparison.OrdinalIgnoreCase))
                {
                    targetId = existing.Id;
                }

                if (!string.Equals(targetId, existing.Id, StringComparison.Ordinal))
                {
                    var clash = TryFindById(current, targetId);
                    if (clash is not null && !ReferenceEquals(clash, existing))
                    {
                        return MutationPlan.Rejected($"Model '{targetId}' already exists.", 409);
                    }

                    var aliasOwner = FindAliasOwner(current, targetId);
                    if (aliasOwner is not null && !ReferenceEquals(aliasOwner, existing))
                    {
                        return MutationPlan.Rejected(
                            $"'{targetId}' is already an alias of model '{aliasOwner.Id}'. " +
                            "Remove that alias first, or choose a different model id.",
                            409);
                    }

                    renamedTo = targetId;
                }

                if (!ModelBackends.TryMergeUpdate(existing, model, out var backends, out var mergeError))
                {
                    return MutationPlan.Rejected(mergeError!, 400);
                }

                // A stored credential is filed under its model and backend, so a reference can only
                // name its own slot. Restating it here follows a rename, and follows a body in the
                // one-url shape onto a backend that is not called "default".
                foreach (var backend in backends)
                {
                    if (!string.IsNullOrWhiteSpace(backend.UpstreamAuth?.SecretRef))
                    {
                        backend.UpstreamAuth!.SecretRef = UpstreamSecretRefs.ForBackend(targetId, backend.Name);
                    }
                }

                var merged = ModelRegistryPersistence.CloneModel(model);
                merged.Backends = backends;
                var updated = ModelRegistryPersistence.CloneModel(merged);
                updated.Id = targetId;

                // Validated after the merge: the body alone does not say what the model's backends
                // will be, and a body in the one-url shape for a model with several has no url to check.
                if (!ModelConfigValidation.TryValidate(updated, out var validationError))
                {
                    return MutationPlan.Rejected(validationError!, 400);
                }

                return MutationPlan.Ok(
                    current
                        .Select(m => string.Equals(m.Id, existing.Id, StringComparison.OrdinalIgnoreCase) ? updated : m)
                        .ToList());
            },
            () => renamedTo is null
                ? $"Model '{id}' updated."
                : $"Model '{id}' renamed to '{renamedTo}' and updated.",
            "update model",
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task<RegistryMutationResult> RemoveModelAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        string? removedId = null;
        IReadOnlyList<string> removedBackends = [];

        var result = await MutateAsync(
            current =>
            {
                var existing = TryFindById(current, id) ?? FindAliasOwner(current, id);
                if (existing is null)
                {
                    return MutationPlan.Rejected($"Model '{id}' was not found.", 404);
                }

                removedId = existing.Id;
                removedBackends = existing.GetBackends().Select(b => b.Name).ToList();

                // Removing the last model is allowed: "no routes configured" is a state an operator
                // is entitled to reach, and refusing it left the row in the database while the admin
                // UI reported a failure the operator could not act on.
                return MutationPlan.Ok(
                    current
                        .Where(m => !string.Equals(m.Id, existing.Id, StringComparison.OrdinalIgnoreCase))
                        .ToList());
            },
            () => $"Model '{removedId}' removed.",
            "remove model",
            cancellationToken).ConfigureAwait(false);

        if (result.Success && removedId is not null)
        {
            await secretStore.DeleteAsync(removedId, cancellationToken).ConfigureAwait(false);
            foreach (var backendName in removedBackends)
            {
                await secretStore
                    .DeleteAsync(UpstreamSecretRefs.StoreKeyForBackend(removedId, backendName), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return result;
    }

    public async Task<RegistryMutationResult> AddBackendAsync(
        string id,
        ModelBackendConfig backend,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(backend);

        string? modelId = null;
        var added = ModelBackends.Clone(backend);
        added.Name = backend.Name?.Trim() ?? string.Empty;

        return await MutateBackendsAsync(
            id,
            (model, backends) =>
            {
                modelId = model.Id;
                if (backends.Any(b => string.Equals(b.Name, added.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return ($"Model '{model.Id}' already has a backend named '{added.Name}'.", 409, false);
                }

                backends.Add(added);
                return (null, 200, true);
            },
            () => $"Backend '{added.Name}' added to model '{modelId}'.",
            "add backend",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RegistryMutationResult> UpdateBackendAsync(
        string id,
        string name,
        ModelBackendConfig backend,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(backend);

        string? modelId = null;
        string? backendName = null;

        return await MutateBackendsAsync(
            id,
            (model, backends) =>
            {
                modelId = model.Id;
                var index = IndexOfBackend(backends, name);
                if (index < 0)
                {
                    return ($"Model '{model.Id}' has no backend named '{name.Trim()}'.", 404, false);
                }

                // The name is the backend's identity (its credential and its health are filed under
                // it) and the state moves only through stop and start, so neither is taken from
                // the body.
                var updated = ModelBackends.Clone(backend);
                updated.Name = backends[index].Name;
                updated.State = backends[index].State;
                backendName = updated.Name;
                backends[index] = updated;
                return (null, 200, true);
            },
            () => $"Backend '{backendName}' of model '{modelId}' updated.",
            "update backend",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RegistryMutationResult> RemoveBackendAsync(
        string id,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string? modelId = null;
        string? backendName = null;
        var left = 0;

        var result = await MutateBackendsAsync(
            id,
            (model, backends) =>
            {
                modelId = model.Id;
                var index = IndexOfBackend(backends, name);
                if (index < 0)
                {
                    return ($"Model '{model.Id}' has no backend named '{name.Trim()}'.", 404, false);
                }

                // Removing the last backend is allowed and leaves the model registered: its
                // grants and its price are decisions people made, and they should not be lost
                // because a container went away.
                backendName = backends[index].Name;
                backends.RemoveAt(index);
                left = backends.Count;
                return (null, 200, true);
            },
            () => left == 0
                ? $"Backend '{backendName}' removed. Model '{modelId}' has no backends left and serves nothing until one is added."
                : $"Backend '{backendName}' removed from model '{modelId}'.",
            "remove backend",
            cancellationToken).ConfigureAwait(false);

        if (result.Success && modelId is not null && backendName is not null)
        {
            await secretStore
                .DeleteAsync(UpstreamSecretRefs.StoreKeyForBackend(modelId, backendName), cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    public async Task<RegistryMutationResult> SetBackendStateAsync(
        string id,
        string name,
        string state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!ModelRouteStates.TryNormalize(state, out var targetState, out var stateError) ||
            string.IsNullOrWhiteSpace(state))
        {
            return RegistryMutationResult.Fail(
                stateError ?? $"state is required and must be one of: {string.Join(", ", ModelRouteStates.All)}.",
                400);
        }

        string? modelId = null;
        string? backendName = null;
        var alreadyInState = false;

        return await MutateBackendsAsync(
            id,
            (model, backends) =>
            {
                modelId = model.Id;
                var index = IndexOfBackend(backends, name);
                if (index < 0)
                {
                    return ($"Model '{model.Id}' has no backend named '{name.Trim()}'.", 404, false);
                }

                backendName = backends[index].Name;
                if (string.Equals(backends[index].State, targetState, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyInState = true;
                    return (null, 200, false);
                }

                backends[index].State = targetState;
                return (null, 200, true);
            },
            () => alreadyInState
                ? $"Backend '{backendName}' of model '{modelId}' is already {targetState}."
                : targetState == ModelRouteStates.Stopped
                    ? $"Backend '{backendName}' of model '{modelId}' stopped; it receives no new requests, and the ones in flight finish."
                    : $"Backend '{backendName}' of model '{modelId}' is serving again.",
            targetState == ModelRouteStates.Stopped ? "stop backend" : "start backend",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One model's backends, changed in place by <paramref name="change"/> on a copy and written
    /// back with everything else about the model, and every other model, as the database had it.
    /// </summary>
    /// <param name="change">
    /// Returns an error and its status to refuse, otherwise whether it changed anything; an
    /// unchanged list is not written.
    /// </param>
    private Task<RegistryMutationResult> MutateBackendsAsync(
        string id,
        Func<ModelConfig, List<ModelBackendConfig>, (string? Error, int StatusCode, bool Changed)> change,
        Func<string> successMessage,
        string action,
        CancellationToken cancellationToken) =>
        MutateAsync(
            current =>
            {
                var existing = TryFindById(current, id) ?? FindAliasOwner(current, id);
                if (existing is null)
                {
                    return MutationPlan.Rejected($"Model '{id}' was not found.", 404);
                }

                var updated = ModelRegistryPersistence.CloneModel(existing);
                var backends = updated.Backends!;
                var (error, statusCode, changed) = change(updated, backends);
                if (error is not null)
                {
                    return MutationPlan.Rejected(error, statusCode);
                }

                if (!changed)
                {
                    return MutationPlan.NoChange();
                }

                // Validated as a whole model, so a backend written here meets every rule one
                // written through the model does.
                if (!ModelConfigValidation.TryValidate(updated, out var validationError))
                {
                    return MutationPlan.Rejected(validationError!, 400);
                }

                ModelBackends.SyncTopLevel(updated);

                return MutationPlan.Ok(
                    current
                        .Select(m => string.Equals(m.Id, existing.Id, StringComparison.OrdinalIgnoreCase) ? updated : m)
                        .ToList());
            },
            successMessage,
            action,
            cancellationToken);

    private static int IndexOfBackend(List<ModelBackendConfig> backends, string name) =>
        backends.FindIndex(b => string.Equals(b.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public async Task<RegistryMutationResult> SetModelStateAsync(
        string id,
        string state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (!ModelRouteStates.TryNormalize(state, out var targetState, out var stateError) ||
            string.IsNullOrWhiteSpace(state))
        {
            // Blank normalizes to "serving" on the write paths that treat it as "unspecified";
            // here the state *is* the request, so an absent one is a caller error, not a default.
            return RegistryMutationResult.Fail(
                stateError ?? $"state is required and must be one of: {string.Join(", ", ModelRouteStates.All)}.",
                400);
        }

        string? targetId = null;
        var alreadyInState = false;

        return await MutateAsync(
            current =>
            {
                var existing = TryFindById(current, id) ?? FindAliasOwner(current, id);
                if (existing is null)
                {
                    return MutationPlan.Rejected($"Model '{id}' was not found.", 404);
                }

                targetId = existing.Id;

                // Idempotent: stopping an already-stopped route is not an error, and rewriting the
                // whole table for a no-op change would bump the route version for every replica.
                if (string.Equals(existing.State, targetState, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyInState = true;
                    return MutationPlan.NoChange();
                }

                var updated = ModelRegistryPersistence.CloneModel(existing);
                updated.State = targetState;

                return MutationPlan.Ok(
                    current
                        .Select(m => string.Equals(m.Id, existing.Id, StringComparison.OrdinalIgnoreCase) ? updated : m)
                        .ToList());
            },
            () => alreadyInState
                ? $"Model '{targetId}' is already {targetState}."
                : targetState == ModelRouteStates.Stopped
                    ? $"Model '{targetId}' stopped; it no longer appears in /v1/models and requests for it are rejected."
                    : $"Model '{targetId}' is serving again.",
            targetState == ModelRouteStates.Stopped ? "stop model" : "start model",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RegistryMutationResult> ReplaceAllAsync(
        IReadOnlyList<ModelConfig> models,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(models);

        // Unlike a targeted remove, a bulk replace with nothing in it is far more likely to be a
        // truncated payload than an intentional "delete every route", so it stays rejected.
        if (models.Count == 0)
        {
            logger.LogWarning("ReplaceAll rejected: empty model list would clear the registry.");
            return RegistryMutationResult.Fail(
                "Cannot replace registry with an empty model list.",
                400);
        }

        foreach (var model in models)
        {
            if (!ModelConfigValidation.TryValidate(model, out var validationError))
            {
                return RegistryMutationResult.Fail(validationError!);
            }
        }

        var cloned = models.Select(ModelRegistryPersistence.CloneModel).ToList();

        return await MutateAsync(
            _ => MutationPlan.Ok(cloned),
            $"Registry replaced with {cloned.Count} model(s).",
            "replace registry",
            cancellationToken).ConfigureAwait(false);
    }

    private Task<RegistryMutationResult> MutateAsync(
        Func<IReadOnlyList<ModelConfig>, MutationPlan> plan,
        string successMessage,
        string action,
        CancellationToken cancellationToken) =>
        MutateAsync(plan, () => successMessage, action, cancellationToken);

    /// <summary>
    /// Read (from the database) → plan → validate → persist (version-checked) → swap memory, all
    /// under the process gate. Every failure mode leaves both the database and the registry untouched.
    /// </summary>
    private async Task<RegistryMutationResult> MutateAsync(
        Func<IReadOnlyList<ModelConfig>, MutationPlan> plan,
        Func<string> successMessage,
        string action,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetService<IModelRouteRepository>();
            if (repository is null)
            {
                return RegistryMutationResult.Fail("Model registry updates require a configured database.", 503);
            }

            ModelRouteSnapshot current;
            try
            {
                current = await repository.ListWithVersionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ModelRegistryPersistErrors.FromException(ex, "database", logger, $"read routes for {action}");
            }

            var planned = plan(current.Models);
            if (!planned.Success)
            {
                return planned.Failure!;
            }

            // A plan that changes nothing must not be persisted. Rewriting the identical route set
            // still bumps the route version, which every other replica polls and reacts to — an
            // idempotent no-op would fan out a pointless reload across the fleet.
            if (planned.Unchanged)
            {
                return RegistryMutationResult.Ok(successMessage());
            }

            // The candidate set must be loadable before it is durable. This is the guard that keeps a
            // rejected write out of the database.
            (Dictionary<string, ModelConfig> Lookup, List<ModelConfig> Models) snapshot;
            try
            {
                snapshot = ModelRegistryPersistence.BuildLookup(planned.Models!);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Rejected {Action}: the resulting registry would not be loadable.", action);
                return RegistryMutationResult.Fail(ex.Message, 400);
            }

            try
            {
                var version = await repository
                    .ReplaceAllAsync(planned.Models!, current.Version, cancellationToken)
                    .ConfigureAwait(false);

                registry.ApplyModels(snapshot, version);
                logger.LogInformation(
                    "Registry {Action}: {ModelCount} model(s) at route version {Version}.",
                    action,
                    planned.Models!.Count,
                    version);

                return RegistryMutationResult.Ok(successMessage());
            }
            catch (ModelRouteVersionConflictException ex)
            {
                logger.LogWarning(
                    "Rejected {Action}: routes changed concurrently (expected version {Expected}, found {Actual}).",
                    action,
                    ex.ExpectedVersion,
                    ex.ActualVersion);
                return RegistryMutationResult.Fail(
                    "The model routes were changed by someone else while this edit was in flight. " +
                    "Reload the model list and try again.",
                    409);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ModelRegistryPersistErrors.FromException(ex, "database", logger, action);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static ModelConfig? TryFindById(IReadOnlyList<ModelConfig> models, string id) =>
        models.FirstOrDefault(m => string.Equals(m.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The model whose alias list claims <paramref name="name"/>, if any. Kept separate from the id
    /// lookup so a name that is only an alias is never reported as an existing model.
    /// </summary>
    private static ModelConfig? FindAliasOwner(IReadOnlyList<ModelConfig> models, string name)
    {
        var trimmed = name.Trim();
        return models.FirstOrDefault(
            m => m.Aliases.Any(alias => string.Equals(alias, trimmed, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed record MutationPlan(
        bool Success,
        List<ModelConfig>? Models,
        RegistryMutationResult? Failure,
        bool Unchanged = false)
    {
        public static MutationPlan Ok(List<ModelConfig> models) => new(true, models, null);

        /// <summary>The request was valid and the registry already satisfies it; nothing to write.</summary>
        public static MutationPlan NoChange() => new(true, null, null, Unchanged: true);

        public static MutationPlan Rejected(string message, int statusCode) =>
            new(false, null, RegistryMutationResult.Fail(message, statusCode));
    }
}
