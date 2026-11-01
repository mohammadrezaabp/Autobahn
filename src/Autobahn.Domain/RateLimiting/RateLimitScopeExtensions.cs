namespace Autobahn.Domain.RateLimiting;
public static class RateLimitScopeExtensions
{
    /// <summary>Stable lower-case label for headers, metrics tags and report rows.</summary>
    public static string ToLabel(this RateLimitScope scope) => scope switch
    {
        RateLimitScope.Global => "global",
        RateLimitScope.Tenant => "tenant",
        RateLimitScope.ApiKey => "api_key",
        RateLimitScope.Model => "model",
        RateLimitScope.TenantModel => "tenant_model",
        RateLimitScope.ApiKeyModel => "api_key_model",
        RateLimitScope.AuthFailure => "auth_failure",
        _ => "unknown",
    };
    /// <summary>
    /// Whether the scope needs the request's model, and therefore belongs to the second evaluation
    /// stage that runs after the body has been parsed.
    /// </summary>
    public static bool RequiresModel(this RateLimitScope scope) => scope is RateLimitScope.Model or RateLimitScope.TenantModel or RateLimitScope.ApiKeyModel;
    /// <summary>
    /// Whether a refusal from this scope is the caller's own doing, rather than a budget it shares
    /// with everybody else.
    /// </summary>
    /// <remarks>
    /// The adaptive governor escalates a partition's <c>Retry-After</c> the longer it keeps being
    /// refused, on the premise that a client refused over and over is retrying too fast. That
    /// premise only holds for a limit the caller alone can exhaust. <see cref = "Global"/> and
    /// <see cref = "Model"/> are shared: a tenant well inside its own tier, sending one request a
    /// minute, was accumulating escalation because the <em>gateway</em> was busy — and then being
    /// told to wait a minute for a limit it was never over. Refusals from a shared scope are still
    /// refusals; they simply say nothing about this caller's pacing.
    /// </remarks>
    public static bool IsCallerScoped(this RateLimitScope scope) => scope is RateLimitScope.Tenant or RateLimitScope.ApiKey or RateLimitScope.TenantModel or RateLimitScope.ApiKeyModel;
}
