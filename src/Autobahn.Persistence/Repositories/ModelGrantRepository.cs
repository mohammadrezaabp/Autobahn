using Microsoft.EntityFrameworkCore;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Persistence.Mapping;

namespace Autobahn.Persistence.Repositories;

public sealed class ModelGrantRepository : IModelGrantRepository
{
    private readonly GatewayDbContext _db;

    public ModelGrantRepository(GatewayDbContext db) => _db = db;

    public async Task<IReadOnlyList<ModelGrantRecord>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _db.ModelGrants
            .AsNoTracking()
            .Where(g => g.TenantId == tenantId)
            .OrderBy(g => g.ModelPattern)
            .ToListAsync(cancellationToken);

        return entities.Select(IdentityEntityMapper.ToRecord).ToList();
    }

    public async Task<ModelGrantRecord> AddAsync(ModelGrantRecord grant, CancellationToken cancellationToken = default)
    {
        var entity = IdentityEntityMapper.ToEntity(grant);
        _db.ModelGrants.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return IdentityEntityMapper.ToRecord(entity);
    }

    public async Task ReplaceForTenantAsync(
        Guid tenantId,
        IReadOnlyList<string> modelPatterns,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.ModelGrants
            .Where(g => g.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        _db.ModelGrants.RemoveRange(existing);

        foreach (var pattern in NormalizePatterns(modelPatterns))
        {
            _db.ModelGrants.Add(new Entities.ModelGrantEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ModelPattern = pattern,
                Effect = GrantEffect.Allow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<string> NormalizePatterns(IReadOnlyList<string> modelPatterns) =>
        modelPatterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
