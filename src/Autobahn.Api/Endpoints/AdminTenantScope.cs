using Microsoft.AspNetCore.Http;
using Autobahn.Domain.Identity;

namespace Autobahn.Api.Endpoints;

/// <summary>
/// The tenant an admin request acts on, and the refusal to use when there is not one.
/// </summary>
/// <remarks>
/// Keys, model grants and usage are all tenant-scoped, so each of their handlers has to resolve a
/// tenant before it can do anything. Three identical private copies of that lookup used to answer
/// <c>401 Unauthorized</c> when it failed, which is the wrong code and had real consequences: the
/// request had already passed <c>RequireAuthorization(Admin)</c>, so authentication succeeded and
/// only the caller's scope was lacking. The admin console reads any 401 as "this credential is
/// dead", so opening the Keys or Usage tab on a gateway with no tenant context tore down the whole
/// session — polling, live stream and all — and told the operator their key had been rejected when
/// it had not been. 403 says what is actually true.
/// </remarks>
internal static class AdminTenantScope
{
    /// <summary>
    /// Distinguishes this refusal from an authentication failure for clients that branch on it.
    /// </summary>
    internal const string DeniedCode = "tenant_context_required";

    internal const string DeniedMessage =
        "This endpoint acts on a tenant's data, and the credential used is not scoped to a tenant. "
        + "A gateway running without a database has no API key store and therefore no tenant, so "
        + "API keys, model grants and usage reporting are unavailable: configure "
        + "ConnectionStrings:GatewayDb and sign in with an admin key issued from it.";

    /// <summary>
    /// The request header with which an operator names another tenant to act on. Checked by
    /// <see cref="Middleware.AdminTenantOverrideMiddleware"/>, never read here.
    /// </summary>
    public const string OverrideHeader = "X-Autobahn-Tenant";

    internal const string OverrideItemKey = "GatewayAdminTenantOverride";

    /// <summary>
    /// The tenant the current request acts on: the caller's own, unless an operator named another.
    /// </summary>
    internal static bool TryResolve(HttpContext context, out Guid tenantId)
    {
        tenantId = default;
        if (!context.Items.TryGetValue(TenantContextKeys.HttpContextItemKey, out var value) ||
            value is not TenantContext tenant)
        {
            return false;
        }

        if (context.Items.TryGetValue(OverrideItemKey, out var named) && named is Guid namedTenantId)
        {
            tenantId = namedTenantId;
            return true;
        }

        return Guid.TryParse(tenant.TenantId, out tenantId);
    }

    /// <summary>True when an operator named the tenant this request acts on.</summary>
    internal static bool IsOverridden(HttpContext context) =>
        context.Items.TryGetValue(OverrideItemKey, out var named) && named is Guid;

    /// <summary>
    /// Authenticated, but not scoped to a tenant. 403 rather than 401 — the credential is fine, it
    /// just cannot reach this resource — and a body naming the cause rather than a bare status.
    /// </summary>
    internal static IResult Denied() => Results.Json(
        new { code = DeniedCode, message = DeniedMessage },
        statusCode: StatusCodes.Status403Forbidden);
}
