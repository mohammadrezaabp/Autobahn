using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;

namespace Autobahn.Security.Services;

public sealed class ModelGrantAdminService : IModelGrantAdminService
{
    private readonly IModelGrantRepository _tenantGrants;
    private readonly IApiKeyModelGrantRepository _apiKeyGrants;
    private readonly IApiKeyRepository _apiKeys;
    private readonly IModelRegistry _registry;
    private readonly IModelGrantService _grantService;

    public ModelGrantAdminService(
        IModelGrantRepository tenantGrants,
        IApiKeyModelGrantRepository apiKeyGrants,
        IApiKeyRepository apiKeys,
        IModelRegistry registry,
        IModelGrantService grantService)
    {
        _tenantGrants = tenantGrants;
        _apiKeyGrants = apiKeyGrants;
        _apiKeys = apiKeys;
        _registry = registry;
        _grantService = grantService;
    }

    public async Task<ModelGrantsResponse> GetTenantGrantsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var grants = await _tenantGrants.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return ToTenantResponse(grants.Select(g => g.ModelPattern).ToList());
    }

    public async Task<ModelGrantsResponse> ReplaceTenantGrantsAsync(
        Guid tenantId,
        ReplaceModelGrantsRequest request,
        CancellationToken cancellationToken = default)
    {
        var modelIds = ValidateModelIds(request.ModelIds);

        // An empty tenant allowlist removes the ceiling entirely rather than removing access, so it
        // must be asked for explicitly. Submitting an empty list reads as "revoke everything" and
        // silently did the opposite: it promoted the tenant from its granted models to every model
        // in the registry.
        if (modelIds.Count == 0 && !request.AllowAllModels)
        {
            throw new ArgumentException(
                "An empty tenant model list removes the tenant ceiling, allowing every model in the "
                + "registry — it does not revoke access. Set allowAllModels=true to confirm that is "
                + "intended. To restrict the tenant instead, submit the models it may use; to remove "
                + "all access, clear the grants on its API keys.",
                nameof(request));
        }

        await _tenantGrants.ReplaceForTenantAsync(tenantId, modelIds, cancellationToken).ConfigureAwait(false);
        _grantService.InvalidateTenantGrants(tenantId);
        return ToTenantResponse(modelIds);
    }

    public async Task<ModelGrantsResponse> GetApiKeyGrantsAsync(
        Guid tenantId,
        Guid apiKeyId,
        CancellationToken cancellationToken = default)
    {
        var key = await RequireInferenceKeyAsync(tenantId, apiKeyId, cancellationToken).ConfigureAwait(false);
        _ = key;
        var grants = await _apiKeyGrants.ListByApiKeyAsync(apiKeyId, cancellationToken).ConfigureAwait(false);
        return ToApiKeyResponse(
            grants.Where(g => g.Effect == GrantEffect.Allow && !ModelGrantPattern.IsWildcard(g.ModelPattern))
                .Select(g => g.ModelPattern).ToList(),
            grants.Where(g => g.Effect == GrantEffect.Allow && ModelGrantPattern.IsWildcard(g.ModelPattern))
                .Select(g => g.ModelPattern).ToList(),
            grants.Where(g => g.Effect == GrantEffect.Deny).Select(g => g.ModelPattern).ToList());
    }

    public async Task<ModelGrantsResponse> ReplaceApiKeyGrantsAsync(
        Guid tenantId,
        Guid apiKeyId,
        ReplaceModelGrantsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireInferenceKeyAsync(tenantId, apiKeyId, cancellationToken).ConfigureAwait(false);
        var modelIds = ValidateModelIds(request.ModelIds);
        var patterns = ValidatePatterns(request.Patterns);
        var denied = ValidateModelIds(request.DeniedModelIds);

        var both = modelIds.Intersect(denied, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (both is not null)
        {
            throw new ArgumentException($"Model '{both}' is both allowed and denied.", nameof(request));
        }

        // Patterns are not checked against the tenant's list here: what they cover changes as
        // models are registered, and the tenant's list is applied to every request anyway.
        await EnsureKeyGrantsWithinTenantAsync(tenantId, modelIds, cancellationToken).ConfigureAwait(false);

        var grants = modelIds.Concat(patterns).Select(p => (p, GrantEffect.Allow))
            .Concat(denied.Select(p => (p, GrantEffect.Deny)))
            .ToList();
        await _apiKeyGrants.ReplaceForApiKeyAsync(apiKeyId, grants, cancellationToken).ConfigureAwait(false);
        _grantService.InvalidateApiKeyGrants(apiKeyId);
        return ToApiKeyResponse(modelIds, patterns, denied);
    }

    private async Task<ApiKeyRecord> RequireInferenceKeyAsync(
        Guid tenantId,
        Guid apiKeyId,
        CancellationToken cancellationToken)
    {
        var key = await _apiKeys.GetByIdAsync(apiKeyId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"API key '{apiKeyId}' was not found.");

        if (key.TenantId != tenantId)
        {
            throw new UnauthorizedAccessException("API key does not belong to the current tenant.");
        }

        if (key.Role is not ApiKeyRole.Inference and not ApiKeyRole.Both)
        {
            throw new InvalidOperationException("Model grants apply only to inference API keys.");
        }

        return key;
    }

    private async Task EnsureKeyGrantsWithinTenantAsync(
        Guid tenantId,
        IReadOnlyList<string> modelIds,
        CancellationToken cancellationToken)
    {
        if (modelIds.Count == 0)
        {
            return;
        }

        var tenantList = await _tenantGrants.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        foreach (var id in modelIds)
        {
            if (!ModelGrantEvaluator.IsModelAllowed(tenantList, id))
            {
                throw new ArgumentException(
                    $"Model '{id}' is not allowed by the tenant model policy.",
                    nameof(modelIds));
            }
        }
    }

    private IReadOnlyList<string> ValidateModelIds(IReadOnlyList<string>? modelIds)
    {
        var canonicalIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawId in modelIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(rawId))
            {
                continue;
            }

            var trimmed = rawId.Trim();
            if (!_registry.TryGetModel(trimmed, out var model) || model is null)
            {
                throw new ArgumentException($"Model '{trimmed}' is not registered.", nameof(modelIds));
            }

            if (seen.Add(model.Id))
            {
                canonicalIds.Add(model.Id);
            }
        }

        return canonicalIds;
    }

    private static IReadOnlyList<string> ValidatePatterns(IReadOnlyList<string>? patterns)
    {
        var valid = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in patterns ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (!ModelGrantPattern.TryValidateWildcard(raw, out var error))
            {
                throw new ArgumentException(error, nameof(patterns));
            }

            var trimmed = raw.Trim();
            if (seen.Add(trimmed))
            {
                valid.Add(trimmed);
            }
        }

        return valid;
    }

    private static ModelGrantsResponse ToTenantResponse(IReadOnlyList<string> modelIds) =>
        new()
        {
            ModelIds = modelIds,
            UsesDefaultAccess = modelIds.Count == 0,
        };

    private static ModelGrantsResponse ToApiKeyResponse(
        IReadOnlyList<string> modelIds,
        IReadOnlyList<string> patterns,
        IReadOnlyList<string> deniedModelIds) =>
        new()
        {
            ModelIds = modelIds,
            Patterns = patterns,
            DeniedModelIds = deniedModelIds,
            UsesDefaultAccess = false,
        };
}
