using Microsoft.EntityFrameworkCore;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Persistence.Entities;
using Autobahn.Persistence.Infrastructure;

namespace Autobahn.Persistence.Repositories;
public sealed class ProviderSettingsRepository(GatewayDbContext dbContext) : IProviderSettingsRepository
{
    public async Task<IReadOnlyList<ProviderSettingsRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.ProviderSettings.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(s => new ProviderSettingsRecord(s.Provider, s.LowBalanceUsd, s.NewModelsStartStopped)).ToList();
    }

    public async Task UpsertAsync(ProviderSettingsRecord settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var entity = await dbContext.ProviderSettings.FirstOrDefaultAsync(s => s.Provider == settings.Provider, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new ProviderSettingsEntity
            {
                Provider = settings.Provider
            };
            dbContext.ProviderSettings.Add(entity);
        }

        entity.LowBalanceUsd = settings.LowBalanceUsd;
        entity.NewModelsStartStopped = settings.NewModelsStartStopped;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
