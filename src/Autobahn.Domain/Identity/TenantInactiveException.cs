namespace Autobahn.Domain.Identity;

/// <summary>
/// Keys were asked to move into a tenant that is inactive. Every key of an inactive tenant is
/// refused at authentication, so the move would switch them off.
/// </summary>
public sealed class TenantInactiveException(string slug)
    : Exception($"Tenant '{slug}' is inactive; keys moved into it would stop authenticating.")
{
    public const string Code = "tenant_inactive";

    public string Slug { get; } = slug;
}
