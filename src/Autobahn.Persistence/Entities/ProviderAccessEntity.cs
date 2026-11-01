namespace Autobahn.Persistence.Entities;

/// <summary>
/// One provider switch: for a tenant when <see cref="ApiKeyId"/> is null, otherwise for that key.
/// A provider with no row is closed.
/// </summary>
public sealed class ProviderAccessEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public TenantEntity Tenant { get; set; } = null!;

    public Guid? ApiKeyId { get; set; }

    public ApiKeyEntity? ApiKey { get; set; }

    public required string Provider { get; set; }

    public bool IsOpen { get; set; }

    /// <summary>USD the tenant, or the key, may spend at the provider in a calendar month; null for no cap.</summary>
    public decimal? MonthlyCapUsd { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
