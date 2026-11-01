namespace Autobahn.Domain.Identity;

/// <summary>A tenant with the requested slug already exists.</summary>
public sealed class TenantSlugConflictException(string slug)
    : Exception($"A tenant with slug '{slug}' already exists.")
{
    public const string Code = "tenant_slug_taken";

    public string Slug { get; } = slug;
}
