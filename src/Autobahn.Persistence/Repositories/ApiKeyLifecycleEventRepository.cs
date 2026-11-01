using Microsoft.EntityFrameworkCore;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Persistence.Mapping;

namespace Autobahn.Persistence.Repositories;

public sealed class ApiKeyLifecycleEventRepository : IApiKeyLifecycleEventRepository
{
    private readonly GatewayDbContext _db;

    public ApiKeyLifecycleEventRepository(GatewayDbContext db) => _db = db;

    public async Task AppendAsync(ApiKeyLifecycleEventRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        _db.ApiKeyLifecycleEvents.Add(IdentityEntityMapper.ToEntity(record));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApiKeyLifecycleEventRecord>> ListForKeyAsync(
        Guid tenantId,
        Guid apiKeyId,
        CancellationToken cancellationToken = default)
    {
        // Filtered on tenant as well as key id: for a permanently deleted key there is no api_keys
        // row left to check ownership against, so this pair is the only thing keeping one tenant's
        // history out of another tenant's reach.
        var entities = await _db.ApiKeyLifecycleEvents
            .AsNoTracking()
            .Where(e => e.ApiKeyId == apiKeyId && e.TenantId == tenantId)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        return entities.Select(IdentityEntityMapper.ToRecord).ToList();
    }
}
