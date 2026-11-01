using Microsoft.EntityFrameworkCore;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Persistence.Mapping;

namespace Autobahn.Persistence.Repositories;

public sealed class BudgetRepository(GatewayDbContext dbContext) : IBudgetRepository
{
    public async Task<IReadOnlyList<BudgetRecord>> GetByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.Budgets
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(BillingEntityMapper.ToRecord).ToList();
    }

    public async Task<IReadOnlyList<BudgetRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.Budgets
            .AsNoTracking()
            .OrderBy(b => b.TenantId)
            .ThenBy(b => b.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(BillingEntityMapper.ToRecord).ToList();
    }
}
