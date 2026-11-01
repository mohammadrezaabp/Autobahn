namespace Autobahn.Application.Security;
public static class GatewayAuthClaims
{
    public const string TenantId = "tenant_id";
    public const string ApiKeyId = "api_key_id";
    public const string Role = "api_key_role";
    /// <summary>
    /// The authenticated tenant's slug. Carried so the operator-tenant check is a pure claims
    /// comparison rather than a database lookup in the authorization path.
    /// </summary>
    public const string TenantSlug = "tenant_slug";
}
