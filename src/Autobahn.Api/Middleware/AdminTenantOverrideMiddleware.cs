using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Autobahn.Api.Endpoints;
using Autobahn.Application.Abstractions;
using Autobahn.Application.Security;

namespace Autobahn.Api.Middleware;

/// <summary>
/// Lets an operator name the tenant an admin request acts on, with the
/// <c>X-Autobahn-Tenant</c> header.
/// </summary>
/// <remarks>
/// Keys, model grants and usage are scoped to the caller's own tenant. Once each team has a tenant
/// of its own, that left the operator able to create tenants and move keys into them but not to
/// see or manage a single key there: the console's key list went from every key to the handful
/// left in the operator tenant. The header is the one place that scope is widened, and only for a
/// caller that already holds the Operator policy, which can move any key into its own tenant
/// anyway. Anyone else sending it is refused rather than ignored, so a tenant admin never gets
/// their own tenant's data back from a request that asked for another's.
///
/// It runs after authentication and ahead of the endpoints, which read the result through
/// <see cref="AdminTenantScope.TryResolve"/>.
/// </remarks>
public sealed class AdminTenantOverrideMiddleware
{
    public const string InvalidCode = "invalid_tenant_header";
    public const string OperatorRequiredCode = "operator_required";
    public const string TenantNotFoundCode = "tenant_not_found";

    private readonly RequestDelegate _next;

    public AdminTenantOverrideMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAuthorizationService authorization,
        ITenantAdminService tenants)
    {
        if (!context.Request.Path.StartsWithSegments("/admin/api") ||
            !context.Request.Headers.TryGetValue(AdminTenantScope.OverrideHeader, out var header))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(header.ToString().Trim(), out var tenantId))
        {
            await RefuseAsync(
                context,
                StatusCodes.Status400BadRequest,
                InvalidCode,
                $"{AdminTenantScope.OverrideHeader} must be a tenant id.").ConfigureAwait(false);
            return;
        }

        // An unauthenticated request is left to the endpoint's own authorization, which answers 401.
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var operatorCheck = await authorization
                .AuthorizeAsync(context.User, context, GatewayAuthPolicies.Operator)
                .ConfigureAwait(false);
            if (!operatorCheck.Succeeded)
            {
                await RefuseAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    OperatorRequiredCode,
                    "Only an operator key can act on another tenant.").ConfigureAwait(false);
                return;
            }

            var known = await tenants.ListAsync(context.RequestAborted).ConfigureAwait(false);
            if (known.All(t => t.Id != tenantId))
            {
                await RefuseAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    TenantNotFoundCode,
                    "No tenant has that id.").ConfigureAwait(false);
                return;
            }

            context.Items[AdminTenantScope.OverrideItemKey] = tenantId;
        }

        await _next(context).ConfigureAwait(false);
    }

    private static Task RefuseAsync(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { code, message }, context.RequestAborted);
    }
}
