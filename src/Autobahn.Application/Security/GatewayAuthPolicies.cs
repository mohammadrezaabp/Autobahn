namespace Autobahn.Application.Security;
public static class GatewayAuthPolicies
{
    public const string Inference = "GatewayInference";
    public const string Admin = "GatewayAdmin";
    /// <summary>
    /// Gateway-wide control-plane access: model registry and upstream credentials, providers, CORS,
    /// rate limits, config reload, backups, and the cross-tenant request/log feeds.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref = "Admin"/>, which is a per-tenant role: any tenant's admin manages its
    /// own keys and grants, and can mint more admin keys for its own tenant. Gating the global
    /// surfaces on the role alone therefore handed every tenant's admin the whole gateway — model
    /// routes, other tenants' request logs, global rate limits. This policy additionally requires
    /// the key to belong to the operator tenant (the bootstrap tenant by default).
    /// </remarks>
    public const string Operator = "GatewayOperator";
}
