using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;
using Autobahn.Application.Security;

namespace Autobahn.Api.Endpoints;

/// <summary>
/// The provider switches of a tenant and its keys, and the access check.
/// </summary>
/// <remarks>
/// Opening a tenant for a paid provider is the operator's decision: it is what lets that tenant
/// spend the company's credit at all. A tenant's own admin then opens individual keys, and can
/// only do so once the tenant is open.
/// </remarks>
public static class AdminProviderAccessEndpoints
{
    public static IEndpointRouteBuilder MapAdminProviderAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/api")
            .RequireAuthorization(GatewayAuthPolicies.Admin);

        group.MapGet("/tenant/provider-access", GetTenantAccessAsync);
        group.MapPut("/tenant/provider-access/{provider}", PutTenantAccessAsync)
            .RequireAuthorization(GatewayAuthPolicies.Operator);

        group.MapGet("/keys/{id:guid}/provider-access", GetApiKeyAccessAsync);
        group.MapPut("/keys/{id:guid}/provider-access/{provider}", PutApiKeyAccessAsync);

        group.MapGet("/access/check", CheckAsync);

        return endpoints;
    }

    private static async Task<IResult> GetTenantAccessAsync(
        HttpContext httpContext,
        IProviderAccessAdminService access,
        CancellationToken cancellationToken)
    {
        if (!AdminTenantScope.TryResolve(httpContext, out var tenantId))
        {
            return AdminTenantScope.Denied();
        }

        return Results.Json(await access.GetTenantAccessAsync(tenantId, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> PutTenantAccessAsync(
        string provider,
        HttpContext httpContext,
        IProviderAccessAdminService access,
        IAuditLogger audit,
        [FromBody] SetProviderAccessRequest? request,
        CancellationToken cancellationToken)
    {
        if (!AdminTenantScope.TryResolve(httpContext, out var tenantId))
        {
            return AdminTenantScope.Denied();
        }

        if (request is null)
        {
            return Results.BadRequest(new { message = "A body with 'open' is required." });
        }

        try
        {
            var change = await access.SetTenantAccessAsync(tenantId, provider, request.Open, cancellationToken)
                .ConfigureAwait(false);
            audit.LogAdminAction(
                "tenant.provider_access.set",
                AdminActor.FromHttpContext(httpContext).ToAuditEntry(
                    new { TenantId = tenantId, change.Provider, change.Open, change.PreviousOpen }));

            if (request.ChangesCap)
            {
                var cap = await access
                    .SetTenantCapAsync(tenantId, provider, request.ClearCap ? null : request.MonthlyCapUsd, cancellationToken)
                    .ConfigureAwait(false);
                audit.LogAdminAction(
                    "tenant.provider_cap.set",
                    AdminActor.FromHttpContext(httpContext).ToAuditEntry(
                        new { TenantId = tenantId, cap.Provider, cap.MonthlyCapUsd, cap.PreviousMonthlyCapUsd }));
            }

            return Results.Json(await access.GetTenantAccessAsync(tenantId, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static Task<IResult> GetApiKeyAccessAsync(
        Guid id,
        HttpContext httpContext,
        IProviderAccessAdminService access,
        CancellationToken cancellationToken) =>
        ForKeyAsync(httpContext, async tenantId =>
            Results.Json(await access.GetApiKeyAccessAsync(tenantId, id, cancellationToken).ConfigureAwait(false)));

    private static Task<IResult> PutApiKeyAccessAsync(
        Guid id,
        string provider,
        HttpContext httpContext,
        IProviderAccessAdminService access,
        IAuditLogger audit,
        [FromBody] SetProviderAccessRequest? request,
        CancellationToken cancellationToken) =>
        ForKeyAsync(httpContext, async tenantId =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { message = "A body with 'open' is required." });
            }

            var change = await access
                .SetApiKeyAccessAsync(tenantId, id, provider, request.Open, cancellationToken)
                .ConfigureAwait(false);
            audit.LogAdminAction(
                "api_key.provider_access.set",
                AdminActor.FromHttpContext(httpContext).ToAuditEntry(
                    new { TenantId = tenantId, KeyId = id, change.Provider, change.Open, change.PreviousOpen }));

            if (request.ChangesCap)
            {
                var cap = await access
                    .SetApiKeyCapAsync(tenantId, id, provider, request.ClearCap ? null : request.MonthlyCapUsd, cancellationToken)
                    .ConfigureAwait(false);
                audit.LogAdminAction(
                    "api_key.provider_cap.set",
                    AdminActor.FromHttpContext(httpContext).ToAuditEntry(
                        new { TenantId = tenantId, KeyId = id, cap.Provider, cap.MonthlyCapUsd, cap.PreviousMonthlyCapUsd }));
            }

            return Results.Json(await access.GetApiKeyAccessAsync(tenantId, id, cancellationToken).ConfigureAwait(false));
        });

    private static Task<IResult> CheckAsync(
        Guid keyId,
        string? model,
        HttpContext httpContext,
        IProviderAccessAdminService access,
        CancellationToken cancellationToken) =>
        ForKeyAsync(httpContext, async tenantId =>
            Results.Json(await access.CheckAsync(tenantId, keyId, model ?? string.Empty, cancellationToken)
                .ConfigureAwait(false)));

    /// <summary>Resolves the tenant and maps the key lookups' failures the way the grant endpoints do.</summary>
    private static async Task<IResult> ForKeyAsync(HttpContext httpContext, Func<Guid, Task<IResult>> handle)
    {
        if (!AdminTenantScope.TryResolve(httpContext, out var tenantId))
        {
            return AdminTenantScope.Denied();
        }

        try
        {
            return await handle(tenantId).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
