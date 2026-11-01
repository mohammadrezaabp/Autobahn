using Autobahn.Domain.Identity;

namespace Autobahn.Application.Abstractions;

public interface IProviderAccessRepository
{
    /// <summary>Every provider switch of a tenant: its own and those of its keys.</summary>
    Task<IReadOnlyList<ProviderAccessRecord>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens or closes a provider for a tenant (<paramref name="apiKeyId"/> null) or one of its keys.
    /// </summary>
    /// <returns>What the switch was before; false when it had never been set.</returns>
    Task<bool> SetAsync(
        Guid tenantId,
        Guid? apiKeyId,
        string provider,
        bool isOpen,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the monthly cap of a tenant (<paramref name="apiKeyId"/> null) or one of its keys at a
    /// provider. Null removes it. The switch is left as it is; a row that did not exist is created
    /// closed.
    /// </summary>
    /// <returns>The cap it replaced.</returns>
    Task<decimal?> SetCapAsync(
        Guid tenantId,
        Guid? apiKeyId,
        string provider,
        decimal? monthlyCapUsd,
        CancellationToken cancellationToken = default);

    /// <summary>Every tenant's own switch for one provider, with the number of its keys that are open.</summary>
    Task<IReadOnlyList<ProviderAccessRecord>> ListByProviderAsync(
        string provider,
        CancellationToken cancellationToken = default);
}
