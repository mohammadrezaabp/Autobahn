using Autobahn.Domain.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Autobahn.Api.Services;
using Autobahn.Application.Providers;
using Autobahn.Application.Security;

namespace Autobahn.Api.Endpoints;

public static class AdminProviderEndpoints
{
    public static IEndpointRouteBuilder MapAdminProviderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/api/providers")
            .RequireAuthorization(GatewayAuthPolicies.Operator);

        group.MapGet("/catalog", ListProviders);

        // The paid providers the gateway manages: state, access, spend, settings, catalogue.
        group.MapGet(string.Empty, ListManagedProviders);
        group.MapGet("/{providerId}/access", GetProviderAccess);
        group.MapPut("/{providerId}/access/{tenantId:guid}", PutProviderAccess);
        group.MapGet("/{providerId}/spend", GetProviderSpend);
        group.MapGet("/{providerId}/reconciliation", GetProviderReconciliation);
        group.MapPut("/{providerId}/settings", PutProviderSettings);
        group.MapPut("/{providerId}/key", PutProviderKey);
        group.MapDelete("/{providerId}/key", DeleteProviderKey);
        group.MapPost("/{providerId}/balance/refresh", RefreshProviderBalance);
        group.MapGet("/{providerId}/catalog", GetProviderCatalog);
        group.MapPost("/{providerId}/catalog/add", AddProviderModels);
        // Provider model discovery is POST-only so env var names never appear in URLs or access logs.
        group.MapPost("/{providerId}/models", PostProviderModels);
        group.MapGet("/{providerId}/models", ProviderModelsGetNotAllowed);
        group.MapPost("/models", PostCustomProviderModels);
        group.MapGet("/models", ProviderModelsGetNotAllowed);
        group.MapPost("/openrouter/models", PostOpenRouterModelsLegacy);
        group.MapGet("/openrouter/models", ProviderModelsGetNotAllowed);

        return endpoints;
    }

    private static Task<IResult> ListManagedProviders(ProviderAdminService providers, CancellationToken cancellationToken) =>
        HandleAsync(async () => Results.Json(await providers.ListAsync(cancellationToken).ConfigureAwait(false)));

    private static Task<IResult> GetProviderAccess(
        string providerId,
        ProviderAdminService providers,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
            Results.Json(await providers.GetTenantAccessAsync(providerId, cancellationToken).ConfigureAwait(false)));

    /// <summary>
    /// Opens or closes a tenant for a provider and sets its monthly cap. The operator's decision:
    /// it is what lets that tenant spend the company's credit at all.
    /// </summary>
    private static Task<IResult> PutProviderAccess(
        string providerId,
        Guid tenantId,
        HttpContext httpContext,
        ProviderAdminService providers,
        Autobahn.Application.Abstractions.IProviderAccessAdminService access,
        Autobahn.Application.Abstractions.IAuditLogger audit,
        [FromBody] Autobahn.Domain.Models.SetProviderAccessRequest? request,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            var provider = ProviderAdminService.RequireProvider(providerId).Id;
            if (request is null)
            {
                return Results.BadRequest(new { message = "A body with 'open' is required." });
            }

            var actor = AdminActor.FromHttpContext(httpContext);
            var change = await access.SetTenantAccessAsync(tenantId, provider, request.Open, cancellationToken)
                .ConfigureAwait(false);
            audit.LogAdminAction(
                "tenant.provider_access.set",
                actor.ToAuditEntry(new { TenantId = tenantId, change.Provider, change.Open, change.PreviousOpen }));

            if (request.ChangesCap)
            {
                var cap = await access
                    .SetTenantCapAsync(tenantId, provider, request.ClearCap ? null : request.MonthlyCapUsd, cancellationToken)
                    .ConfigureAwait(false);
                audit.LogAdminAction(
                    "tenant.provider_cap.set",
                    actor.ToAuditEntry(new { TenantId = tenantId, cap.Provider, cap.MonthlyCapUsd, cap.PreviousMonthlyCapUsd }));
            }

            return Results.Json(await providers.GetTenantAccessAsync(provider, cancellationToken).ConfigureAwait(false));
        });

    private static Task<IResult> GetProviderSpend(
        string providerId,
        int? days,
        ProviderAdminService providers,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
            Results.Json(await providers.GetSpendAsync(providerId, days ?? 30, cancellationToken).ConfigureAwait(false)));

    private static Task<IResult> GetProviderReconciliation(
        string providerId,
        int? days,
        ProviderAdminService providers,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
            Results.Json(await providers.GetReconciliationAsync(providerId, days, cancellationToken).ConfigureAwait(false)));

    private static Task<IResult> PutProviderSettings(
        string providerId,
        HttpContext httpContext,
        ProviderAdminService providers,
        Autobahn.Application.Abstractions.IAuditLogger audit,
        [FromBody] Autobahn.Domain.Models.SetProviderSettingsRequest? request,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { message = "A body is required." });
            }

            var saved = await providers.SetSettingsAsync(providerId, request, cancellationToken).ConfigureAwait(false);
            audit.LogAdminAction(
                "provider.settings.set",
                AdminActor.FromHttpContext(httpContext).ToAuditEntry(
                    new { saved.Provider, saved.LowBalanceUsd, saved.NewModelsStartStopped }));
            return Results.Json(await providers.ListAsync(cancellationToken).ConfigureAwait(false));
        });

    private static Task<IResult> PutProviderKey(
        string providerId,
        HttpContext httpContext,
        ProviderAdminService providers,
        Autobahn.Application.Abstractions.IAuditLogger audit,
        [FromBody] Autobahn.Domain.Models.SetProviderKeyRequest? request,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            await providers.SetKeyAsync(providerId, request?.ApiKey, cancellationToken).ConfigureAwait(false);

            // That a key was replaced, and by whom. Never the key, nor any part of it.
            audit.LogAdminAction(
                "provider.key.set",
                AdminActor.FromHttpContext(httpContext).ToAuditEntry(new { Provider = providerId }));
            return Results.Json(await providers.ListAsync(cancellationToken).ConfigureAwait(false));
        });

    private static Task<IResult> DeleteProviderKey(
        string providerId,
        HttpContext httpContext,
        ProviderAdminService providers,
        Autobahn.Application.Abstractions.IAuditLogger audit,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            await providers.ClearKeyAsync(providerId, cancellationToken).ConfigureAwait(false);
            audit.LogAdminAction(
                "provider.key.clear",
                AdminActor.FromHttpContext(httpContext).ToAuditEntry(new { Provider = providerId }));
            return Results.Json(await providers.ListAsync(cancellationToken).ConfigureAwait(false));
        });

    private static Task<IResult> RefreshProviderBalance(
        string providerId,
        ProviderAdminService providers,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
            Results.Json(await providers.RefreshBalanceAsync(providerId, cancellationToken).ConfigureAwait(false)));

    private static Task<IResult> GetProviderCatalog(
        string providerId,
        string? q,
        ProviderAdminService providers,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
            Results.Json(await providers.GetCatalogAsync(providerId, q, cancellationToken).ConfigureAwait(false)));

    private static Task<IResult> AddProviderModels(
        string providerId,
        HttpContext httpContext,
        ProviderAdminService providers,
        Autobahn.Application.Abstractions.IAuditLogger audit,
        [FromBody] Autobahn.Domain.Models.AddProviderModelsRequest? request,
        CancellationToken cancellationToken) =>
        HandleAsync(async () =>
        {
            var actor = AdminActor.FromHttpContext(httpContext);
            var result = await providers
                .AddModelsAsync(providerId, request?.Models ?? [], actor, cancellationToken)
                .ConfigureAwait(false);
            audit.LogAdminAction(
                "provider.models.add",
                actor.ToAuditEntry(new
                {
                    Provider = providerId,
                    Added = result.Results.Where(r => r.Added).Select(r => r.Id).ToList(),
                    Refused = result.Results.Where(r => !r.Added).Select(r => r.Id).ToList(),
                }));
            return Results.Json(result);
        });

    private static async Task<IResult> HandleAsync(Func<Task<IResult>> handle)
    {
        try
        {
            return await handle().ConfigureAwait(false);
        }
        catch (ProviderAdminException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static IResult ListProviders()
    {
        var providers = ProviderCatalog.ListBuiltIn()
            .Select(ToProviderListDto)
            .ToList();

        providers.Add(ToProviderListDto(
            new ProviderDefinition(
                ProviderCatalog.CustomProviderId,
                "Custom (OpenAI-compatible)",
                string.Empty,
                string.Empty,
                string.Empty,
                RequiresUpstreamAuth: false)));

        return Results.Json(new { data = providers });
    }

    private static IResult ProviderModelsGetNotAllowed() =>
        Results.Problem(
            detail: "Provider model discovery requires POST with a JSON body. Do not pass envVar or API keys in query strings.",
            statusCode: StatusCodes.Status405MethodNotAllowed);

    private static object ToProviderListDto(ProviderDefinition p) => new
    {
        id = p.Id,
        displayName = p.DisplayName,
        upstreamBaseUrl = p.UpstreamBaseUrl,
        modelsListUrl = p.ModelsListUrl,
        defaultEnvVar = p.DefaultEnvVar,
        requiresUpstreamAuth = p.RequiresUpstreamAuth,
        supportsDiscovery = p.SupportsDiscovery,
    };

    private static Task<IResult> PostProviderModels(
        string providerId,
        [FromBody] ProviderModelsDiscoveryRequest? body,
        IConfiguration configuration,
        UpstreamEnvVarPolicy envVarPolicy,
        OpenAiCompatibleProviderModelsClient client,
        CancellationToken cancellationToken) =>
        DiscoverProviderModelsAsync(providerId, configuration, envVarPolicy, client, body?.EnvVar, cancellationToken);

    private static async Task<IResult> DiscoverProviderModelsAsync(
        string providerId,
        IConfiguration configuration,
        UpstreamEnvVarPolicy envVarPolicy,
        OpenAiCompatibleProviderModelsClient client,
        string? envVar,
        CancellationToken cancellationToken)
    {
        if (string.Equals(providerId, ProviderCatalog.CustomProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                detail: "Use POST /admin/api/providers/models with modelsUrl for custom providers.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ProviderCatalog.TryGetBuiltIn(providerId, out var definition) || definition is null)
        {
            return Results.Problem(
                detail: $"Unknown provider '{providerId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!definition.SupportsDiscovery)
        {
            return Results.Problem(
                detail: $"Model discovery is not available for local provider '{definition.DisplayName}'; "
                    + $"add models by id using upstream base URL {definition.UpstreamBaseUrl}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? resolvedEnvVar;
        if (string.IsNullOrWhiteSpace(envVar))
        {
            resolvedEnvVar = definition.DefaultEnvVar;
        }
        else if (!EnvVarNameValidator.TryValidate(envVar, out var normalized, out var validationError))
        {
            return Results.Problem(detail: validationError, statusCode: StatusCodes.Status400BadRequest);
        }
        else
        {
            resolvedEnvVar = normalized;
        }

        if (definition.RequiresUpstreamAuth && string.IsNullOrWhiteSpace(resolvedEnvVar))
        {
            return Results.Problem(
                detail: $"Provider '{definition.DisplayName}' requires an envVar for the upstream API key.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!string.IsNullOrWhiteSpace(resolvedEnvVar) &&
            !envVarPolicy.IsAllowed(resolvedEnvVar, out var policyError))
        {
            return Results.Problem(detail: policyError, statusCode: StatusCodes.Status400BadRequest);
        }

        var token = ResolveBearerToken(configuration, resolvedEnvVar);
        if (definition.RequiresUpstreamAuth && string.IsNullOrWhiteSpace(token))
        {
            return Results.Problem(
                detail: $"Missing API token. Set environment variable '{resolvedEnvVar}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!Uri.TryCreate(definition.ModelsListUrl, UriKind.Absolute, out var modelsListUri))
        {
            return Results.Problem(
                detail: "Provider models list URL is invalid.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return await ListModelsOrUpstreamProblemAsync(
            () => client.ListModelsAsync(modelsListUri, token ?? string.Empty, cancellationToken),
            models => Results.Json(new
            {
                provider = definition.Id,
                upstreamBaseUrl = definition.UpstreamBaseUrl,
                data = models,
            })).ConfigureAwait(false);
    }

    private static Task<IResult> PostCustomProviderModels(
        [FromBody] CustomProviderModelsDiscoveryRequest? body,
        IConfiguration configuration,
        UpstreamEnvVarPolicy envVarPolicy,
        OpenAiCompatibleProviderModelsClient client,
        CancellationToken cancellationToken) =>
        DiscoverCustomProviderModelsAsync(
            configuration,
            envVarPolicy,
            client,
            body?.ModelsUrl,
            body?.EnvVar,
            cancellationToken);

    private static async Task<IResult> DiscoverCustomProviderModelsAsync(
        IConfiguration configuration,
        UpstreamEnvVarPolicy envVarPolicy,
        OpenAiCompatibleProviderModelsClient client,
        string? modelsUrl,
        string? envVar,
        CancellationToken cancellationToken)
    {
        if (!ProviderModelsListUrlValidator.TryValidate(modelsUrl, out var modelsListUri, out var urlError))
        {
            return Results.Problem(detail: urlError, statusCode: StatusCodes.Status400BadRequest);
        }

        string? bearerToken = null;
        if (!string.IsNullOrWhiteSpace(envVar))
        {
            if (!EnvVarNameValidator.TryValidate(envVar, out var resolvedEnvVar, out var envError))
            {
                return Results.Problem(detail: envError, statusCode: StatusCodes.Status400BadRequest);
            }

            // The caller chose both the variable to read and the URL to send it to, so without this
            // check the endpoint would forward any secret in the gateway's environment to any host.
            if (!envVarPolicy.IsAllowed(resolvedEnvVar, out var policyError))
            {
                return Results.Problem(detail: policyError, statusCode: StatusCodes.Status400BadRequest);
            }

            bearerToken = ResolveBearerToken(configuration, resolvedEnvVar);
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                return Results.Problem(
                    detail: $"Missing API token. Set environment variable '{resolvedEnvVar}'.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var upstreamBaseUrl = DeriveUpstreamBaseUrl(modelsListUri!);

        return await ListModelsOrUpstreamProblemAsync(
            () => client.ListModelsAsync(modelsListUri!, bearerToken ?? string.Empty, cancellationToken),
            models => Results.Json(new
            {
                provider = ProviderCatalog.CustomProviderId,
                upstreamBaseUrl,
                data = models,
            })).ConfigureAwait(false);
    }

    private static async Task<IResult> ListModelsOrUpstreamProblemAsync(
        Func<Task<IReadOnlyList<ProviderModelListItem>>> listModels,
        Func<IReadOnlyList<ProviderModelListItem>, IResult> onSuccess)
    {
        try
        {
            var models = await listModels().ConfigureAwait(false);
            return onSuccess(models);
        }
        catch (ProviderModelsUpstreamException ex)
        {
            return Results.Problem(
                title: "Upstream model list failed",
                detail: $"The provider returned HTTP {(int)ex.StatusCode} ({ex.StatusCode}).",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static Task<IResult> PostOpenRouterModelsLegacy(
        [FromBody] ProviderModelsDiscoveryRequest? body,
        IConfiguration configuration,
        UpstreamEnvVarPolicy envVarPolicy,
        OpenAiCompatibleProviderModelsClient client,
        CancellationToken cancellationToken) =>
        DiscoverProviderModelsAsync("openrouter", configuration, envVarPolicy, client, body?.EnvVar, cancellationToken);

    private static string? ResolveBearerToken(IConfiguration configuration, string? envVar)
    {
        if (string.IsNullOrWhiteSpace(envVar))
        {
            return null;
        }

        return configuration[envVar.Trim()]
            ?? Environment.GetEnvironmentVariable(envVar.Trim());
    }

    private static string DeriveUpstreamBaseUrl(Uri modelsListUri)
    {
        var path = modelsListUri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/v1/models", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^"/v1/models".Length];
        }
        else if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^"/models".Length];
        }

        var builder = new UriBuilder(modelsListUri.Scheme, modelsListUri.Host, modelsListUri.Port, path);
        return builder.Uri.ToString().TrimEnd('/');
    }
}
