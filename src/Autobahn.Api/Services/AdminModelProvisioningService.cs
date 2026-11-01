using Autobahn.Domain.Validation;
using Microsoft.Extensions.DependencyInjection;
using Autobahn.Api.Contracts;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;
using Autobahn.Application.Providers;

namespace Autobahn.Api.Services;

public sealed class AdminModelProvisioningService(
    IControlPlaneCommands commands,
    IUpstreamSecretStore secretStore,
    IServiceScopeFactory scopeFactory,
    UpstreamEnvVarPolicy envVarPolicy,
    IAuditLogger audit)
{
    /// <summary>
    /// Pricing is backed by the database and so is scoped, while this service is a singleton.
    /// Resolve it per call from a fresh scope, as ModelRegistryWriter does for its repository.
    /// </summary>
    private async Task<T> WithPricingAsync<T>(Func<IRateCardAdminService, Task<T>> action)
    {
        using var scope = scopeFactory.CreateScope();
        var pricing = scope.ServiceProvider.GetRequiredService<IRateCardAdminService>();
        return await action(pricing).ConfigureAwait(false);
    }

    public Task<RegistryMutationResult> AddAsync(
        AdminModelWriteRequest request,
        CancellationToken cancellationToken = default) =>
        AddAsync(request, AdminActor.Anonymous, cancellationToken);

    /// <param name="request">The model write.</param>
    /// <param name="actor">The caller, stamped on every audit entry the write produces.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task<RegistryMutationResult> AddAsync(
        AdminModelWriteRequest request,
        AdminActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        var model = request.Model ?? throw new ArgumentException("Model is required.");

        var prep = PrepareModel(
            model,
            request.ApiKey,
            request.ClearApiKey,
            isUpdate: false,
            existing: null);
        if (!prep.Success)
        {
            return RegistryMutationResult.Fail(prep.Error!);
        }

        // Secret first, model second. A model registered with a secretRef whose secret does not
        // exist is silently broken — every inference request fails with "upstream auth token not
        // configured" and nothing surfaces the cause.
        //
        // The store is keyed by model id, and the id may already belong to a registered model —
        // that is exactly the case AddModelAsync rejects with 409 below. So the secret write here
        // may be overwriting (or, with clearApiKey, deleting) a live credential, not creating a
        // fresh one. Snapshot the prior value first: a rejected add must put the store back exactly
        // as it was, where deleting "the" secret destroyed the existing model's credential.
        var hadPriorSecret = secretStore.TryGet(prep.SecretKey!, out var priorSecret);

        if (!await TryApplySecretAsync(prep, actor, cancellationToken).ConfigureAwait(false))
        {
            return RegistryMutationResult.Fail(
                "The upstream credential could not be stored, so the model was not created.");
        }

        var result = await commands.AddModelAsync(prep.Model!, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            await RestoreSecretAfterFailedAddAsync(prep, hadPriorSecret, priorSecret, actor, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }

        return await ApplyPricingAsync(prep.Model!.Id, request, result, actor, cancellationToken).ConfigureAwait(false);
    }

    public Task<RegistryMutationResult> UpdateAsync(
        string id,
        AdminModelWriteRequest request,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(id, request, AdminActor.Anonymous, cancellationToken);

    /// <param name="id">The route id or alias of the model to update.</param>
    /// <param name="request">The model write.</param>
    /// <param name="actor">The caller, stamped on every audit entry the write produces.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task<RegistryMutationResult> UpdateAsync(
        string id,
        AdminModelWriteRequest request,
        AdminActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        var model = request.Model ?? throw new ArgumentException("Model is required.");

        // The route parameter may be an alias, so resolve the model the write will actually land on:
        // a rename has to move that model's credential and rate card, both of which are keyed by the
        // canonical id.
        var existing = ResolveRegisteredModel(id);

        var prep = PrepareModel(
            model,
            request.ApiKey,
            request.ClearApiKey,
            isUpdate: true,
            existing: existing);
        if (!prep.Success)
        {
            return RegistryMutationResult.Fail(prep.Error!);
        }

        // Update keeps the model-first order: the model already exists and is serving traffic, so
        // rewriting its credential before knowing the metadata update is accepted would disturb a
        // working model on a validation failure. A secret failure here is reported rather than
        // swallowed, so the operator knows the credential did not take effect.
        var result = await commands.UpdateModelAsync(id, prep.Model!, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return result;
        }

        var previousId = existing?.Id;
        var renamedFrom = previousId is not null &&
                          !string.Equals(previousId, prep.Model!.Id, StringComparison.OrdinalIgnoreCase)
            ? previousId
            : null;

        if (!await TryApplySecretAsync(prep, actor, cancellationToken).ConfigureAwait(false))
        {
            return RegistryMutationResult.Fail(
                $"{result.Message} However, the upstream credential was not stored, so this model " +
                "cannot authenticate to its upstream until the credential is set again.");
        }

        if (renamedFrom is not null)
        {
            await MigrateRenamedModelAsync(renamedFrom, prep, request, actor, cancellationToken).ConfigureAwait(false);
        }

        return await ApplyPricingAsync(prep.Model!.Id, request, result, actor, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AdminModelListItem>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var pricingByModel = await WithPricingAsync(p => p.GetPricingByModelAsync(cancellationToken))
            .ConfigureAwait(false);

        var models = commands.ListModels();

        // One bulk existence query for the whole list. This used to be a blocking
        // ExistsAsync(...).GetAwaiter().GetResult() per model, which pinned a thread-pool thread per
        // model on every admin list request and shares that pool with the inference path.
        var secretRefModelIds = models
            .SelectMany(m => m.GetBackends())
            .Select(b => b.UpstreamAuth?.SecretRef)
            .Where(secretRef => !string.IsNullOrWhiteSpace(secretRef))
            .Select(secretRef => UpstreamSecretRefs.TryParseModelId(secretRef!, out var id) ? id : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var storedSecrets = secretRefModelIds.Count == 0
            ? (IReadOnlySet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : await secretStore.ListExistingAsync(secretRefModelIds, cancellationToken).ConfigureAwait(false);

        return models
            .Select(m => new AdminModelListItem
            {
                Model = m,
                Provider = ModelProviders.Resolve(m),
                HasUpstreamCredential = HasCredential(m, storedSecrets),
                Pricing = pricingByModel.TryGetValue(m.Id, out var price) ? price : null
            })
            .ToList();
    }

    /// <summary>
    /// Whether a single model has a usable upstream credential. Prefer the list path, which resolves
    /// stored secrets in bulk; this overload exists for callers dealing with one model.
    /// </summary>
    public async Task<bool> HasCredentialAsync(ModelConfig model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.UpstreamAuth is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(model.UpstreamAuth.EnvVar))
        {
            return true;
        }

        return UpstreamSecretRefs.TryParseModelId(model.UpstreamAuth.SecretRef, out var modelId) &&
               await secretStore.ExistsAsync(modelId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the registered model an admin route parameter refers to, by id or by alias, so callers
    /// can work with the canonical id the registry actually stores.
    /// </summary>
    private ModelConfig? ResolveRegisteredModel(string id)
    {
        var trimmed = id.Trim();
        var models = commands.ListModels();

        return models.FirstOrDefault(m => string.Equals(m.Id, trimmed, StringComparison.OrdinalIgnoreCase))
               ?? models.FirstOrDefault(
                   m => m.Aliases.Any(alias => string.Equals(alias, trimmed, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Moves the things keyed by model id — the upstream credential and the rate card — after a
    /// rename. Without this a renamed model loses its credential (and silently stops authenticating)
    /// and its price (and silently bills at zero), while the old id keeps orphaned copies of both.
    /// </summary>
    private async Task MigrateRenamedModelAsync(
        string previousId,
        PrepResult prep,
        AdminModelWriteRequest request,
        AdminActor actor,
        CancellationToken cancellationToken)
    {
        var newId = prep.Model!.Id;

        // Every backend's credential is filed under the model id, so each one moves. The primary
        // backend's is left alone when this same request cleared it or replaced it: the new value
        // is already under the new id, and copying the old one across would undo that.
        foreach (var backend in prep.Model.GetBackends())
        {
            var previousKey = UpstreamSecretRefs.StoreKeyForBackend(previousId, backend.Name);
            var newKey = UpstreamSecretRefs.StoreKeyForBackend(newId, backend.Name);
            var writtenByThisRequest =
                string.Equals(newKey, prep.SecretKey, StringComparison.OrdinalIgnoreCase) &&
                (prep.ClearSecret || !string.IsNullOrWhiteSpace(prep.SecretToStore));

            try
            {
                if (writtenByThisRequest)
                {
                    await secretStore.DeleteAsync(previousKey, cancellationToken).ConfigureAwait(false);
                }
                else if (secretStore.TryGet(previousKey, out var secret) && !string.IsNullOrWhiteSpace(secret))
                {
                    await secretStore.PutAsync(newKey, secret!, cancellationToken).ConfigureAwait(false);
                    await secretStore.DeleteAsync(previousKey, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                audit.LogAdminAction(
                    "model.rename.credential_migration_failed",
                    actor.ToAuditEntry(new { previousId, newId, backend = backend.Name, error = ex.Message }));
            }
        }

        // An explicit pricing instruction in the request is applied to the new id by
        // ApplyPricingAsync; only carry the old rate card over when the request said nothing.
        if (request.Pricing is null && !request.ClearPricing)
        {
            var pricingByModel = await WithPricingAsync(p => p.GetPricingByModelAsync(cancellationToken))
                .ConfigureAwait(false);

            if (pricingByModel.TryGetValue(previousId, out var previousPricing))
            {
                await WithPricingAsync(p => p.SetPricingAsync(newId, previousPricing, cancellationToken))
                    .ConfigureAwait(false);
            }
        }

        await WithPricingAsync(p => p.ClearPricingAsync(previousId, cancellationToken)).ConfigureAwait(false);

        audit.LogAdminAction(
            "model.renamed",
            actor.ToAuditEntry(new { previousId, newId }));
    }

    /// <summary>
    /// Applies pricing after the model itself is persisted, since pricing keys off the model id.
    /// A pricing failure is surfaced rather than swallowed, but the model change already stood.
    /// </summary>
    private async Task<RegistryMutationResult> ApplyPricingAsync(
        string modelId,
        AdminModelWriteRequest request,
        RegistryMutationResult modelResult,
        AdminActor actor,
        CancellationToken cancellationToken)
    {
        if (request.ClearPricing)
        {
            var cleared = await WithPricingAsync(p => p.ClearPricingAsync(modelId, cancellationToken))
                .ConfigureAwait(false);
            return cleared.Success
                ? modelResult
                : RegistryMutationResult.Fail(
                    $"{modelResult.Message} However, pricing was not cleared: {cleared.Message}",
                    cleared.StatusCode);
        }

        if (request.Pricing is null)
        {
            return modelResult;
        }

        var applied = await WithPricingAsync(p => p.SetPricingAsync(modelId, request.Pricing, cancellationToken))
            .ConfigureAwait(false);

        if (!applied.Success)
        {
            return RegistryMutationResult.Fail(
                $"{modelResult.Message} However, pricing was not saved: {applied.Message}",
                applied.StatusCode);
        }

        audit.LogAdminAction(
            "model.pricing.update",
            actor.ToAuditEntry(new
            {
                modelId,
                request.Pricing.InputPricePerMillionTokens,
                request.Pricing.OutputPricePerMillionTokens,
            }));

        return modelResult;
    }

    /// <summary>
    /// Resolves credential presence against an already-fetched set of stored secret ids, so listing
    /// N models costs one secret-store call rather than N.
    /// </summary>
    private static bool HasCredential(ModelConfig model, IReadOnlySet<string> storedSecrets)
    {
        if (model.UpstreamAuth is null)
        {
            return false;
        }

        // An env-var credential is resolved from the process environment, not the secret store.
        if (!string.IsNullOrWhiteSpace(model.UpstreamAuth.EnvVar))
        {
            return true;
        }

        return UpstreamSecretRefs.TryParseModelId(model.UpstreamAuth.SecretRef, out var modelId) &&
               storedSecrets.Contains(modelId);
    }

    private PrepResult PrepareModel(
        ModelConfig model,
        string? apiKey,
        bool clearApiKey,
        bool isUpdate,
        ModelConfig? existing)
    {
        var previousId = existing?.Id;
        var previousState = existing?.State;

        if (!ModelTypes.TryNormalize(model.ModelType, out var modelType, out var modelTypeError))
        {
            return PrepResult.Fail(modelTypeError!);
        }

        // A body that leaves the provider out keeps the one an operator set earlier, for the same
        // reason an update keeps the state: the admin drawer omits what it does not edit.
        if (!ModelProviders.TryNormalize(model.Provider, out var provider, out var providerError))
        {
            return PrepResult.Fail(providerError!);
        }

        provider ??= existing?.Provider;

        // An update never changes the route's state, however the body spells it: state moves only
        // through the dedicated stop/start endpoints. ModelConfig.State defaults to "serving", so a
        // body that simply omits the field — which is every write the admin drawer makes — is
        // indistinguishable from one that asks for "serving". Honouring it would put a stopped route
        // back into service on any unrelated edit (a url fix, a new alias). On create there is no
        // prior state to protect, so an explicit one is taken at face value.
        var requestedState = isUpdate ? previousState : model.State;
        if (!ModelRouteStates.TryNormalize(requestedState, out var state, out var stateError))
        {
            return PrepResult.Fail(stateError!);
        }

        var normalized = new ModelConfig
        {
            Id = model.Id?.Trim() ?? string.Empty,
            Url = model.Url?.Trim() ?? string.Empty,
            MaxContextLength = model.MaxContextLength > 0 ? model.MaxContextLength : 8192,
            Aliases = model.Aliases ?? [],
            PublicAccess = model.PublicAccess,
            Capabilities = model.Capabilities ?? [],
            ModelType = modelType,
            Provider = provider,
            State = state,
            Labels = model.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
        };

        var key = apiKey?.Trim();
        var hasKey = !string.IsNullOrWhiteSpace(key);

        // The backends this write leaves the model with. A body carrying a list says so itself. A
        // body in the one-url shape describes the only backend of a model that has at most one;
        // for a model with several it cannot, so their list is kept and the body may change only
        // what belongs to the model.
        var existingBackends = existing?.GetBackends() ?? [];
        List<ModelBackendConfig>? backends = null;
        if (model.Backends is not null)
        {
            backends = model.Backends.Where(b => b is not null).Select(ModelBackends.Clone).ToList();
            foreach (var backend in backends)
            {
                backend.Name = string.IsNullOrWhiteSpace(backend.Name) ? ModelBackends.DefaultName : backend.Name.Trim();
                backend.Url = backend.Url?.Trim() ?? string.Empty;
            }
        }
        else if (existingBackends.Count > 1)
        {
            if (hasKey || clearApiKey)
            {
                return PrepResult.Fail(
                    $"Model '{existing!.Id}' has {existingBackends.Count} backends. Set or clear a credential on one of them " +
                    "through /backends/{name}.");
            }

            if (!string.IsNullOrWhiteSpace(normalized.Url) &&
                !string.Equals(normalized.Url, existingBackends[0].Url?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return PrepResult.Fail(
                    $"Model '{existing!.Id}' has {existingBackends.Count} backends, so a single url cannot describe it. " +
                    "Send 'backends', or change one backend through /backends/{name}.");
            }

            backends = existingBackends.Select(ModelBackends.Clone).ToList();
        }
        else if (existingBackends.Count == 1 && !string.IsNullOrWhiteSpace(normalized.Url))
        {
            var only = ModelBackends.Clone(existingBackends[0]);
            only.Url = normalized.Url;
            only.MaxContextLength = normalized.MaxContextLength;
            only.UpstreamAuth = ModelBackends.CloneAuth(model.UpstreamAuth);
            backends = [only];
        }

        else if (isUpdate && existing is not null && existingBackends.Count == 0 &&
                 string.IsNullOrWhiteSpace(normalized.Url))
        {
            // A model left with no backends is still edited — its aliases, its price — without
            // being given one.
            backends = [];
        }

        if (string.IsNullOrWhiteSpace(normalized.Id) ||
            (backends is null && string.IsNullOrWhiteSpace(normalized.Url)))
        {
            return PrepResult.Fail("Model id and url are required.");
        }

        // The request's apiKey belongs to the primary backend: the model's only one, or the first.
        var primary = backends?.FirstOrDefault();
        var primaryName = primary?.Name ?? ModelBackends.DefaultName;
        var keepPrimaryAuth = model.Backends is null && existingBackends.Count > 1;
        var sourceAuth = backends is null ? model.UpstreamAuth : primary?.UpstreamAuth;
        var secretKey = UpstreamSecretRefs.StoreKeyForBackend(normalized.Id, primaryName);
        string? secretToStore = null;

        if ((hasKey || clearApiKey) && backends is { Count: 0 })
        {
            return PrepResult.Fail("This model has no backends, so there is nothing to set a credential on.");
        }

        if (backends is not null)
        {
            // Every backend but the primary: its credential comes from its own entry.
            foreach (var backend in backends.Skip(1))
            {
                if (!TryNormalizeBackendAuth(backend, normalized.Id, out var backendAuthError))
                {
                    return PrepResult.Fail($"backend '{backend.Name}': {backendAuthError}");
                }
            }
        }

        PrepResult Finish(bool clearSecret)
        {
            if (backends is not null)
            {
                if (primary is not null && !keepPrimaryAuth)
                {
                    primary.UpstreamAuth = normalized.UpstreamAuth;
                }

                normalized.Backends = backends;
                ModelBackends.SyncTopLevel(normalized);
            }

            if (!ModelConfigValidation.TryValidate(normalized, out var validationError))
            {
                return PrepResult.Fail(validationError!);
            }

            return PrepResult.Ok(normalized, secretToStore, clearSecret, secretKey);
        }

        if (keepPrimaryAuth)
        {
            return Finish(clearSecret: false);
        }

        if (clearApiKey)
        {
            normalized.UpstreamAuth = null;
            return Finish(clearSecret: true);
        }

        if (hasKey)
        {
            if (LooksLikeInvalidApiKeyPlacement(key!))
            {
                return PrepResult.Fail("apiKey must be the upstream provider secret, not an environment variable name.");
            }

            normalized.UpstreamAuth = new UpstreamAuthConfig
            {
                Type = "bearer",
                SecretRef = UpstreamSecretRefs.ForBackend(normalized.Id, primaryName)
            };

            // Falls through to the shared validation gate below rather than returning early: an
            // early return meant any rule added to ModelConfigValidation silently did not apply to
            // models created with an inline apiKey, which is the most common admin flow.
            secretToStore = key;
        }

        if (!hasKey && sourceAuth is not null)
        {
            if (!string.Equals(sourceAuth.Type, "bearer", StringComparison.OrdinalIgnoreCase))
            {
                return PrepResult.Fail("upstreamAuth.type must be 'bearer'.");
            }

            var hasEnv = !string.IsNullOrWhiteSpace(sourceAuth.EnvVar);
            var hasRef = !string.IsNullOrWhiteSpace(sourceAuth.SecretRef);

            if (hasEnv)
            {
                if (!EnvVarNameValidator.TryValidate(sourceAuth.EnvVar, out var normalizedEnvVar, out var envError))
                {
                    return PrepResult.Fail(envError!);
                }

                if (!envVarPolicy.IsAllowed(normalizedEnvVar, out var policyError))
                {
                    return PrepResult.Fail(policyError!);
                }

                normalized.UpstreamAuth = new UpstreamAuthConfig
                {
                    Type = "bearer",
                    EnvVar = normalizedEnvVar!
                };
            }
            else if (hasRef)
            {
                var secretRef = sourceAuth.SecretRef.Trim();

                // On a rename the client echoes back the credential reference for the old id. Follow
                // the model to its new id rather than rejecting the edit; the stored secret is moved
                // to match once the rename is persisted.
                if (previousId is not null &&
                    !string.Equals(previousId, normalized.Id, StringComparison.Ordinal) &&
                    UpstreamSecretRefs.IsValidForBackend(secretRef, previousId, primaryName))
                {
                    secretRef = UpstreamSecretRefs.ForBackend(normalized.Id, primaryName);
                }

                normalized.UpstreamAuth = new UpstreamAuthConfig
                {
                    Type = "bearer",
                    SecretRef = secretRef
                };
            }
            else if (!isUpdate)
            {
                normalized.UpstreamAuth = null;
            }
            else
            {
                return PrepResult.Fail("upstreamAuth requires envVar or secretRef when not supplying apiKey.");
            }
        }
        else if (!hasKey && !isUpdate)
        {
            normalized.UpstreamAuth = null;
        }

        return Finish(clearSecret: false);
    }

    /// <summary>
    /// Checks and tidies the credential a backend entry names for itself: an environment variable
    /// must pass the policy, and a stored-secret reference is restated for the backend's own slot,
    /// which is the only one it may name (and what follows a model rename).
    /// </summary>
    private bool TryNormalizeBackendAuth(ModelBackendConfig backend, string modelId, out string? error)
    {
        error = null;
        var auth = backend.UpstreamAuth;
        if (auth is null)
        {
            return true;
        }

        if (!string.Equals(auth.Type, "bearer", StringComparison.OrdinalIgnoreCase))
        {
            error = "upstreamAuth.type must be 'bearer'.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(auth.EnvVar))
        {
            if (!EnvVarNameValidator.TryValidate(auth.EnvVar, out var normalizedEnvVar, out error))
            {
                return false;
            }

            if (!envVarPolicy.IsAllowed(normalizedEnvVar, out error))
            {
                return false;
            }

            backend.UpstreamAuth = new UpstreamAuthConfig { Type = "bearer", EnvVar = normalizedEnvVar! };
            return true;
        }

        if (!string.IsNullOrWhiteSpace(auth.SecretRef))
        {
            backend.UpstreamAuth = new UpstreamAuthConfig
            {
                Type = "bearer",
                SecretRef = UpstreamSecretRefs.ForBackend(modelId, backend.Name),
            };
            return true;
        }

        backend.UpstreamAuth = null;
        return true;
    }

    /// <summary>
    /// Adds one backend to a model. The credential is stored first, as for a new model, and put
    /// back if the registry refuses the backend.
    /// </summary>
    public async Task<RegistryMutationResult> AddBackendAsync(
        string id,
        AdminBackendWriteRequest request,
        AdminActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        var model = ResolveRegisteredModel(id);
        if (model is null)
        {
            return RegistryMutationResult.Fail($"Model '{id}' was not found.", 404);
        }

        var prep = PrepareBackend(model, request, existing: null);
        if (!prep.Success)
        {
            return RegistryMutationResult.Fail(prep.Error!);
        }

        var hadPriorSecret = secretStore.TryGet(prep.SecretKey!, out var priorSecret);
        if (!await TryApplySecretAsync(prep, actor, cancellationToken).ConfigureAwait(false))
        {
            return RegistryMutationResult.Fail(
                "The upstream credential could not be stored, so the backend was not added.");
        }

        var result = await commands.AddBackendAsync(model.Id, prep.Backend!, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            await RestoreSecretAfterFailedAddAsync(prep, hadPriorSecret, priorSecret, actor, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Changes one backend. The body's backend replaces its address, context length and labels; a
    /// blank url keeps the address it has, and a body that names no credential keeps the one it has.
    /// </summary>
    public async Task<RegistryMutationResult> UpdateBackendAsync(
        string id,
        string name,
        AdminBackendWriteRequest request,
        AdminActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        var model = ResolveRegisteredModel(id);
        if (model is null)
        {
            return RegistryMutationResult.Fail($"Model '{id}' was not found.", 404);
        }

        var existing = model.FindBackend(name);
        if (existing is null)
        {
            return RegistryMutationResult.Fail($"Model '{model.Id}' has no backend named '{name.Trim()}'.", 404);
        }

        var prep = PrepareBackend(model, request, existing);
        if (!prep.Success)
        {
            return RegistryMutationResult.Fail(prep.Error!);
        }

        var result = await commands.UpdateBackendAsync(model.Id, existing.Name, prep.Backend!, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return result;
        }

        if (!await TryApplySecretAsync(prep, actor, cancellationToken).ConfigureAwait(false))
        {
            return RegistryMutationResult.Fail(
                $"{result.Message} However, the upstream credential was not stored, so this backend " +
                "cannot authenticate to its upstream until the credential is set again.");
        }

        return result;
    }

    private PrepResult PrepareBackend(ModelConfig model, AdminBackendWriteRequest request, ModelBackendConfig? existing)
    {
        var source = request.Backend ?? new ModelBackendConfig();
        var name = existing?.Name ?? source.Name?.Trim() ?? string.Empty;
        if (!ModelBackends.IsValidName(name))
        {
            return PrepResult.Fail(
                "backend.name is required: 1 to 63 letters, digits, '.', '_' or '-', starting with a letter or digit.");
        }

        var backend = new ModelBackendConfig
        {
            Name = name,
            Url = string.IsNullOrWhiteSpace(source.Url) ? existing?.Url ?? string.Empty : source.Url.Trim(),
            MaxContextLength = source.MaxContextLength,
            State = existing?.State ?? source.State,
            UpstreamModel = string.IsNullOrWhiteSpace(source.UpstreamModel) ? null : source.UpstreamModel.Trim(),
            Priority = source.Priority,
            Labels = source.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
            UpstreamAuth = ModelBackends.CloneAuth(source.UpstreamAuth ?? existing?.UpstreamAuth),
        };

        if (string.IsNullOrWhiteSpace(backend.Url))
        {
            return PrepResult.Fail("backend.url is required.");
        }

        var secretKey = UpstreamSecretRefs.StoreKeyForBackend(model.Id, name);
        var key = request.ApiKey?.Trim();
        string? secretToStore = null;
        var clearSecret = false;

        if (request.ClearApiKey)
        {
            backend.UpstreamAuth = null;
            clearSecret = true;
        }
        else if (!string.IsNullOrWhiteSpace(key))
        {
            if (LooksLikeInvalidApiKeyPlacement(key))
            {
                return PrepResult.Fail("apiKey must be the upstream provider secret, not an environment variable name.");
            }

            backend.UpstreamAuth = new UpstreamAuthConfig
            {
                Type = "bearer",
                SecretRef = UpstreamSecretRefs.ForBackend(model.Id, name),
            };
            secretToStore = key;
        }
        else if (!TryNormalizeBackendAuth(backend, model.Id, out var authError))
        {
            return PrepResult.Fail(authError!);
        }

        if (!ModelConfigValidation.TryValidateBackend(backend, model.Id, out var validationError))
        {
            return PrepResult.Fail(validationError!);
        }

        return PrepResult.Ok(model, secretToStore, clearSecret, secretKey) with { Backend = backend };
    }


    /// <summary>
    /// Applies the secret-store side of a provisioning request. Returns false if the store rejected
    /// the write, so the caller can abort or compensate rather than leaving a model whose credential
    /// silently does not exist.
    /// </summary>
    private async Task<bool> TryApplySecretAsync(PrepResult prep, AdminActor actor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prep.Model?.Id))
        {
            return true;
        }

        try
        {
            if (prep.ClearSecret)
            {
                await secretStore.DeleteAsync(prep.SecretKey!, cancellationToken).ConfigureAwait(false);
                audit.LogAdminAction(
                    "upstream_secret.deleted",
                    actor.ToAuditEntry(new { modelId = prep.Model.Id, backend = prep.BackendName }));
                return true;
            }

            if (!string.IsNullOrWhiteSpace(prep.SecretToStore))
            {
                await secretStore.PutAsync(prep.SecretKey!, prep.SecretToStore, cancellationToken).ConfigureAwait(false);
                audit.LogAdminAction(
                    "upstream_secret.updated",
                    actor.ToAuditEntry(new { modelId = prep.Model.Id, backend = prep.BackendName }));
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            audit.LogAdminAction(
                "upstream_secret.failed",
                actor.ToAuditEntry(new
                {
                    modelId = prep.Model.Id,
                    backend = prep.BackendName,
                    operation = prep.ClearSecret ? "delete" : "put",
                    error = ex.Message,
                }));
            return false;
        }
    }

    /// <summary>
    /// Puts the secret store back the way it was before a model create that was then rejected.
    /// </summary>
    /// <remarks>
    /// Restores, not deletes: when the rejected id belongs to an existing model (the 409 duplicate
    /// case), the secret this create overwrote or cleared was that model's <em>live credential</em>,
    /// and deleting it broke every subsequent request to the model with "upstream auth token not
    /// configured". Only when nothing was stored under the id before does rollback mean removal.
    /// A restore failure is audited: at that point the store disagrees with the registry, which is
    /// worth surfacing rather than inert.
    /// </remarks>
    private async Task RestoreSecretAfterFailedAddAsync(
        PrepResult prep,
        bool hadPriorSecret,
        string? priorSecret,
        AdminActor actor,
        CancellationToken cancellationToken)
    {
        var touchedStore = prep.ClearSecret || !string.IsNullOrWhiteSpace(prep.SecretToStore);
        if (!touchedStore || string.IsNullOrWhiteSpace(prep.Model?.Id))
        {
            return;
        }

        try
        {
            if (hadPriorSecret && !string.IsNullOrEmpty(priorSecret))
            {
                await secretStore.PutAsync(prep.SecretKey!, priorSecret, cancellationToken).ConfigureAwait(false);
                audit.LogAdminAction(
                    "upstream_secret.restored",
                    actor.ToAuditEntry(new { modelId = prep.Model.Id, backend = prep.BackendName }));
            }
            else if (!prep.ClearSecret)
            {
                await secretStore.DeleteAsync(prep.SecretKey!, cancellationToken).ConfigureAwait(false);
                audit.LogAdminAction(
                    "upstream_secret.rolled_back",
                    actor.ToAuditEntry(new { modelId = prep.Model.Id, backend = prep.BackendName }));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            audit.LogAdminAction(
                "upstream_secret.rollback_failed",
                actor.ToAuditEntry(new { modelId = prep.Model.Id, backend = prep.BackendName, error = ex.Message }));
        }
    }

    /// <summary>
    /// Detects an environment variable <em>name</em> pasted into the <c>apiKey</c> field.
    /// </summary>
    /// <remarks>
    /// The signal is the shouting-snake-case convention environment variables use
    /// (<c>OPENROUTER_API_KEY</c>), not "parses as an identifier". Accepting the latter rejected real
    /// credentials: any token built from letters, digits and underscores — a Hugging Face
    /// <c>hf_…</c> token, for one — satisfies the identifier grammar, so the most common admin flow
    /// (paste the provider key) refused a whole class of valid keys and told the operator they had
    /// entered a variable name.
    /// </remarks>
    internal static bool LooksLikeInvalidApiKeyPlacement(string value)
    {
        var trimmed = value.Trim();

        // Real credentials are long. A short all-caps identifier is a variable name.
        if (trimmed.Length is 0 or > 64)
        {
            return false;
        }

        if (!EnvVarNameValidator.TryValidate(trimmed, out _, out _))
        {
            return false;
        }

        foreach (var c in trimmed)
        {
            if (char.IsLower(c))
            {
                return false;
            }
        }

        // All-caps, underscore-separated, and it names a credential — the environment variable shape.
        return trimmed.Contains('_', StringComparison.Ordinal);
    }

    /// <param name="SecretKey">Where this write's credential is stored: the model's primary backend, or the one backend being written.</param>
    private sealed record PrepResult(
        bool Success,
        ModelConfig? Model,
        string? SecretToStore,
        bool ClearSecret,
        string? Error,
        string? SecretKey = null)
    {
        /// <summary>Set by the backend writes: the one backend the request is about.</summary>
        public ModelBackendConfig? Backend { get; init; }

        public string BackendName =>
            Backend?.Name ?? Model?.GetBackends().FirstOrDefault()?.Name ?? ModelBackends.DefaultName;

        public static PrepResult Ok(ModelConfig model, string? secretToStore, bool clearSecret, string secretKey) =>
            new(true, model, secretToStore, clearSecret, null, secretKey);

        public static PrepResult Fail(string error) =>
            new(false, null, null, false, error);
    }
}
