using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;
using Autobahn.Application.Security;

namespace Autobahn.Api.Endpoints;

/// <summary>
/// Tenant management. Operator-only: unlike the key endpoints, which act on the caller's own
/// tenant, these create tenants and move keys across them, so a tenant's own admin must not reach
/// them.
/// </summary>
public static class AdminTenantEndpoints
{
    public static IEndpointRouteBuilder MapAdminTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/api/tenants")
            .RequireAuthorization(GatewayAuthPolicies.Operator);

        group.MapGet("/", ListTenantsAsync);
        group.MapPost("/", CreateTenantAsync);
        group.MapPut("/{id:guid}", UpdateTenantAsync);
        group.MapPost("/{id:guid}/keys/move", MoveKeysAsync);
        return endpoints;
    }

    private static async Task<IResult> ListTenantsAsync(
        HttpContext httpContext,
        ITenantAdminService tenants,
        CancellationToken cancellationToken)
    {
        // The caller's tenant is not what these act on; resolving it is how a gateway running
        // without a database answers with the same refusal as the other tenant-backed endpoints.
        if (!AdminTenantScope.TryResolve(httpContext, out _))
        {
            return AdminTenantScope.Denied();
        }

        return Results.Json(await tenants.ListAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> CreateTenantAsync(
        CreateAdminTenantRequest request,
        HttpContext httpContext,
        ITenantAdminService tenants,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        if (!AdminTenantScope.TryResolve(httpContext, out var actorTenantId))
        {
            return AdminTenantScope.Denied();
        }

        try
        {
            var created = await tenants.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            audit.LogAdminAction(
                "tenant.create",
                new AuditLogEntry(
                    actorTenantId.ToString(),
                    GetActorKeyId(httpContext)?.ToString(),
                    new { TenantId = created.Id, created.Slug, created.Name, created.PlanSlug, created.CostCenter }));

            return Results.Json(created, statusCode: StatusCodes.Status201Created);
        }
        catch (TenantSlugConflictException ex)
        {
            return Results.Json(
                new { code = TenantSlugConflictException.Code, message = ex.Message },
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> UpdateTenantAsync(
        Guid id,
        UpdateAdminTenantRequest request,
        HttpContext httpContext,
        ITenantAdminService tenants,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        if (!AdminTenantScope.TryResolve(httpContext, out var actorTenantId))
        {
            return AdminTenantScope.Denied();
        }

        try
        {
            var updated = await tenants.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
            audit.LogAdminAction(
                "tenant.update",
                new AuditLogEntry(
                    actorTenantId.ToString(),
                    GetActorKeyId(httpContext)?.ToString(),
                    new { TenantId = id, updated.Slug, updated.Name, updated.PlanSlug, updated.CostCenter }));

            return Results.Json(updated);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> MoveKeysAsync(
        Guid id,
        MoveAdminApiKeysRequest request,
        HttpContext httpContext,
        ITenantAdminService tenants,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        if (!AdminTenantScope.TryResolve(httpContext, out var actorTenantId))
        {
            return AdminTenantScope.Denied();
        }

        try
        {
            var result = await tenants
                .MoveKeysAsync(id, request.KeyIds, GetActorKeyId(httpContext), cancellationToken)
                .ConfigureAwait(false);

            // Written even when nothing moved: a refused attempt on a tenant's last admin key is
            // worth a line in the trail.
            audit.LogAdminAction(
                "tenant.keys.move",
                new AuditLogEntry(
                    actorTenantId.ToString(),
                    GetActorKeyId(httpContext)?.ToString(),
                    new
                    {
                        TargetTenantId = result.TenantId,
                        TargetTenantSlug = result.TenantSlug,
                        Moved = result.Moved.Select(k => new { k.Id, k.KeyPrefix, k.Label, k.FromTenantId }),
                        Skipped = result.Skipped.Select(k => new { k.Id, k.Code }),
                    }));

            return Results.Json(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (TenantInactiveException ex)
        {
            return Results.Json(
                new { code = TenantInactiveException.Code, message = ex.Message },
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static Guid? GetActorKeyId(HttpContext context) =>
        Guid.TryParse(context.User.FindFirst(GatewayAuthClaims.ApiKeyId)?.Value, out var id) ? id : null;
}
