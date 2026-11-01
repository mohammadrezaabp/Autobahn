using Autobahn.Domain.Billing;

namespace Autobahn.Application.Abstractions;
public interface IProviderSpendRepository
{
    /// <summary>Adds each delta to its bucket, atomically with respect to other writers.</summary>
    Task IncrementAsync(IReadOnlyList<ProviderSpendDelta> deltas, CancellationToken cancellationToken = default);
    /// <summary>
    /// The buckets of the days <paramref name = "from"/> to <paramref name = "to"/>, both included,
    /// narrowed to a provider and to a tenant when given.
    /// </summary>
    Task<IReadOnlyList<ProviderSpendRecord>> ListAsync(DateOnly from, DateOnly to, string? provider = null, Guid? tenantId = null, CancellationToken cancellationToken = default);
}
