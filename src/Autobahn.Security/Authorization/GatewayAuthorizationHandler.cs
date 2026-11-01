using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Application.Security;
using Autobahn.Security.Authentication;
using Autobahn.Security.Configuration;

namespace Autobahn.Security.Authorization;
public sealed class GatewayAuthorizationHandler : AuthorizationHandler<GatewayAuthorizationRequirement>
{
    private readonly IGatewayAuthenticationState _authState;
    private readonly OperatorTenantConfiguration _operatorTenant;
    public GatewayAuthorizationHandler(IGatewayAuthenticationState authState, OperatorTenantConfiguration operatorTenant)
    {
        _authState = authState;
        _operatorTenant = operatorTenant;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, GatewayAuthorizationRequirement requirement)
    {
        // Authentication disabled is an *inference* mode, not an administrative one. It exists so a
        // gateway with no key store can still serve models; the control plane is not part of that
        // bargain. Succeeding every policy here is what made a DB-less host serve /admin/api to
        // anyone who could reach the port. Admin and Operator therefore fall through to the
        // enforcing path below, where an anonymous caller is denied — and in this mode nothing can
        // authenticate (NullApiKeyValidator fails every credential), so they are denied outright.
        // That is deliberate: DB-less anonymous mode is an intentionally limited mode, not a
        // fully functional administrative one.
        if (!_authState.IsAuthenticationRequired && requirement.PolicyName == GatewayAuthPolicies.Inference)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.Resource is HttpContext httpContext && requirement.PolicyName == GatewayAuthPolicies.Inference && !PublicModelAccess.HasRejectedCredential(httpContext) && (PublicModelAccess.IsPublicInferenceRequest(httpContext) || PublicModelAccess.AllowsAnonymousModelsListing(httpContext)))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            return Task.CompletedTask;
        }

        var roleClaim = context.User.FindFirst(GatewayAuthClaims.Role)?.Value;
        if (!Enum.TryParse<ApiKeyRole>(roleClaim, out var role))
        {
            return Task.CompletedTask;
        }

        var allowed = requirement.PolicyName switch
        {
            GatewayAuthPolicies.Admin => role is ApiKeyRole.Admin or ApiKeyRole.Both,
            // Role alone is per-tenant; the gateway-wide control plane additionally requires the key
            // to belong to the operator tenant. Without the second check, any tenant's admin could
            // rewrite model routes, upstream secrets, global rate limits, and read every tenant's
            // recent requests.
            GatewayAuthPolicies.Operator => (role is ApiKeyRole.Admin or ApiKeyRole.Both) && _operatorTenant.IsOperatorTenant(context.User.FindFirst(GatewayAuthClaims.TenantSlug)?.Value),
            GatewayAuthPolicies.Inference => role is ApiKeyRole.Inference or ApiKeyRole.Both,
            _ => false,
        };
        if (allowed)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
