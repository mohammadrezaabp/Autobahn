using Autobahn.Domain.Billing;

namespace Autobahn.Application.Abstractions;

public interface IBudgetRepository
{
    Task<IReadOnlyList<BudgetRecord>> GetByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Every budget across every tenant — the operator's Overview reads these; enforcement never does.</summary>
    Task<IReadOnlyList<BudgetRecord>> GetAllAsync(CancellationToken cancellationToken = default);
}
