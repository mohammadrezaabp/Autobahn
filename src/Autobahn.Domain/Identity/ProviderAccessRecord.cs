namespace Autobahn.Domain.Identity;

/// <summary>
/// Whether a paid provider is open for a tenant (<see cref="ApiKeyId"/> null) or for one of its
/// keys, and how much it may spend there in a calendar month. No record means closed.
/// </summary>
/// <param name="MonthlyCapUsd">Null is no cap of its own; a key is then held only by its tenant's.</param>
public sealed record ProviderAccessRecord(
    Guid Id,
    Guid TenantId,
    Guid? ApiKeyId,
    string Provider,
    bool IsOpen,
    decimal? MonthlyCapUsd = null);
