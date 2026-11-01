namespace Autobahn.Domain.Models;
public static class MoveApiKeySkipCodes
{
    public const string NotFound = "not_found";
    public const string AlreadyInTenant = "already_in_tenant";
    /// <summary>The key the caller is authenticating with.</summary>
    public const string SelfAction = "self_action";
    /// <summary>The key is its current tenant's last way into its own control plane.</summary>
    public const string LastAdminKey = "last_admin_key";
}
