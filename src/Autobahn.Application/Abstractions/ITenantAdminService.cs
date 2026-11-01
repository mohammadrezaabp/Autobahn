using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

/// <summary>
/// Gateway-wide tenant management: creating tenants, editing them, and moving API keys between
/// them. Unlike <see cref="IAdminKeyService"/> nothing here is scoped to the caller's own tenant,
/// so every caller must already hold the Operator policy.
/// </summary>
public interface ITenantAdminService
{
    Task<IReadOnlyList<AdminTenantListItem>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a tenant. Throws <see cref="ArgumentException"/> for an invalid slug, name or plan
    /// and <see cref="Identity.TenantSlugConflictException"/> when the slug is taken.
    /// </summary>
    Task<AdminTenantListItem> CreateAsync(
        CreateAdminTenantRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the tenant's name, plan and cost center. The slug is not editable. Throws
    /// <see cref="KeyNotFoundException"/> when the tenant does not exist.
    /// </summary>
    Task<AdminTenantListItem> UpdateAsync(
        Guid tenantId,
        UpdateAdminTenantRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves keys into <paramref name="targetTenantId"/>. A key that may not move is reported in
    /// <see cref="MoveAdminApiKeysResponse.Skipped"/> instead of failing the batch. Throws
    /// <see cref="KeyNotFoundException"/> when the target tenant does not exist and
    /// <see cref="Identity.TenantInactiveException"/> when it is inactive.
    /// </summary>
    /// <remarks>
    /// The key keeps its secret, role, per-key model grants and per-key rate-limit rules. From its
    /// next request it is admitted against the target tenant's bucket, grants and budget. Usage
    /// already billed stays with the tenant that incurred it.
    /// </remarks>
    Task<MoveAdminApiKeysResponse> MoveKeysAsync(
        Guid targetTenantId,
        IReadOnlyCollection<Guid> keyIds,
        Guid? actorKeyId = null,
        CancellationToken cancellationToken = default);
}
