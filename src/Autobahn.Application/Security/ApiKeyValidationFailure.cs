namespace Autobahn.Application.Security;
public enum ApiKeyValidationFailure
{
    /// <summary>No credential was presented.</summary>
    Missing,
    /// <summary>A credential was presented and it matches no stored key.</summary>
    Invalid,
    Expired,
    Revoked,
    /// <summary>The key matched a stored record whose tenant is deactivated.</summary>
    TenantInactive,
}
