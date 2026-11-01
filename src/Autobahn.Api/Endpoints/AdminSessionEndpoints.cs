using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Autobahn.Api.Contracts;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Domain.RateLimiting;
using Autobahn.Application.Security;

namespace Autobahn.Api.Endpoints;

/// <summary>
/// What a tenant's own admin key may read about itself: who it is signed in as, and the limits
/// that apply to its tenant. Both sit behind the Admin policy, not Operator, so the console has
/// something to sign such a key in with and something to show it.
/// </summary>
public static class AdminSessionEndpoints
{
    public static IEndpointRouteBuilder MapAdminSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/api")
            .RequireAuthorization(GatewayAuthPolicies.Admin);

        group.MapGet("/session", GetSessionAsync);
        group.MapGet("/tenant/rate-limits", GetTenantRateLimitsAsync);
        return endpoints;
    }

    /// <summary>
    /// The key's own tenant and whether it is an operator. Always the caller's tenant: the
    /// <c>X-Autobahn-Tenant</c> header names what a request acts on, not who is asking.
    /// </summary>
    private static async Task<IResult> GetSessionAsync(
        HttpContext httpContext,
        IAuthorizationService authorization,
        [FromServices] ITenantRepository? tenants,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(httpContext.User.FindFirst(GatewayAuthClaims.TenantId)?.Value, out var tenantId))
        {
            return AdminTenantScope.Denied();
        }

        var operatorCheck = await authorization
            .AuthorizeAsync(httpContext.User, httpContext, GatewayAuthPolicies.Operator)
            .ConfigureAwait(false);
        var slug = httpContext.User.FindFirst(GatewayAuthClaims.TenantSlug)?.Value;
        var tenant = tenants is null
            ? null
            : await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return Results.Json(new
        {
            tenantId,
            tenantSlug = tenant?.Slug ?? slug,
            tenantName = tenant?.Name ?? slug,
            apiKeyId = httpContext.User.FindFirst(GatewayAuthClaims.ApiKeyId)?.Value,
            role = httpContext.User.FindFirst(GatewayAuthClaims.Role)?.Value,
            @operator = operatorCheck.Succeeded,
        });
    }

    /// <summary>
    /// The part of the rate-limit configuration that is this tenant's: the tier its shared bucket
    /// starts from, and the rules on the tenant and on its keys. Read-only.
    /// </summary>
    /// <remarks>
    /// The body has the shape of <c>GET /admin/api/rate-limits</c> so one reader serves both, with
    /// everything that is another tenant's left out: other plans, other tenants' and keys' rules,
    /// and the rules on a model for everyone and on the whole gateway. Those last two also limit
    /// this tenant, and stay the operator's to show.
    /// </remarks>
    private static async Task<IResult> GetTenantRateLimitsAsync(
        HttpContext httpContext,
        IRateLimitConfigAdminService service,
        IAdminKeyService adminKeys,
        [FromServices] ITenantRepository? tenants,
        CancellationToken cancellationToken)
    {
        // A gateway without a database registers no tenant store. Nothing authenticates there, so
        // this is not reached; the parameter is optional so that such a gateway still starts.
        if (tenants is null || !AdminTenantScope.TryResolve(httpContext, out var tenantId))
        {
            return AdminTenantScope.Denied();
        }

        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return Results.NotFound();
        }

        var keys = await adminKeys
            .ListAsync(tenantId, includeArchived: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            subjects.Add(key.Id.ToString());
        }

        var config = service.GetCurrent();
        var plans = new Dictionary<string, RateLimitTierOptions>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(tenant.PlanSlug) &&
            config.Plans.FirstOrDefault(p => string.Equals(p.Key, tenant.PlanSlug.Trim(), StringComparison.OrdinalIgnoreCase))
                is { Key: not null } plan)
        {
            plans[plan.Key] = plan.Value;
        }

        return Results.Json(new
        {
            tenant = new { id = tenant.Id, slug = tenant.Slug, name = tenant.Name, planSlug = tenant.PlanSlug },
            version = config.Version,
            enabled = config.Enabled,
            @default = config.Default,
            plans,
            rules = config.Rules
                .Where(rule => IsOfTenant(rule, tenant.Id.ToString(), tenant.Slug, subjects))
                .Select(AdminRateLimitRuleDto.FromDefinition)
                .ToArray(),
        });
    }

    /// <summary>A tenant rule names the tenant by id or by slug; a key rule names the key by id.</summary>
    private static bool IsOfTenant(
        RateLimitRuleDefinition rule,
        string tenantId,
        string tenantSlug,
        HashSet<string> keyIds)
    {
        var target = rule.TargetKey ?? string.Empty;
        var subject = RateLimitScopeNames.IsPair(rule.Scope) ? target.Split('|')[0] : target;
        subject = subject.Trim();

        return RateLimitScopeNames.Canonical(rule.Scope) switch
        {
            RateLimitScopeNames.Tenant or RateLimitScopeNames.TenantModel =>
                string.Equals(subject, tenantId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(subject, tenantSlug, StringComparison.OrdinalIgnoreCase),
            RateLimitScopeNames.ApiKey or RateLimitScopeNames.ApiKeyModel => keyIds.Contains(subject),
            _ => false,
        };
    }
}
