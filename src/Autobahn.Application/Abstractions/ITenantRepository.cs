using Autobahn.Domain.Identity;

namespace Autobahn.Application.Abstractions;

public interface ITenantRepository
{
    Task<TenantRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TenantRecord?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<TenantRecord> CreateAsync(TenantRecord tenant, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantRecord>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Every tenant, inactive ones included, ordered by slug.</summary>
    /// <remarks>Throws when unimplemented: an empty list would read as "no tenants exist".</remarks>
    Task<IReadOnlyList<TenantRecord>> ListAllAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not implement {nameof(ListAllAsync)}.");

    /// <summary>
    /// Replaces the tenant's name, plan and cost center and stamps <c>UpdatedAt</c>. The slug is
    /// left alone: rate-limit rules and metric labels are keyed on it.
    /// </summary>
    /// <remarks>Throws when unimplemented, so a write is never reported that did not happen.</remarks>
    Task<TenantRecord> UpdateAsync(
        Guid id,
        string name,
        string? planSlug,
        string? costCenter,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not implement {nameof(UpdateAsync)}.");
}
