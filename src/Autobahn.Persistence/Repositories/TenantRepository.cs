using Microsoft.EntityFrameworkCore;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Persistence.Mapping;

namespace Autobahn.Persistence.Repositories;

public sealed class TenantRepository : ITenantRepository
{
    private readonly GatewayDbContext _db;

    public TenantRepository(GatewayDbContext db) => _db = db;

    public async Task<TenantRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return entity is null ? null : IdentityEntityMapper.ToRecord(entity);
    }

    public async Task<TenantRecord?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var entity = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);

        return entity is null ? null : IdentityEntityMapper.ToRecord(entity);
    }

    public async Task<TenantRecord> CreateAsync(TenantRecord tenant, CancellationToken cancellationToken = default)
    {
        var entity = IdentityEntityMapper.ToEntity(tenant);
        _db.Tenants.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return IdentityEntityMapper.ToRecord(entity);
    }

    public async Task<IReadOnlyList<TenantRecord>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Slug)
            .ToListAsync(cancellationToken);

        return entities.Select(IdentityEntityMapper.ToRecord).ToList();
    }

    public async Task<IReadOnlyList<TenantRecord>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _db.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Slug)
            .ToListAsync(cancellationToken);

        return entities.Select(IdentityEntityMapper.ToRecord).ToList();
    }

    public async Task<TenantRecord> UpdateAsync(
        Guid id,
        string name,
        string? planSlug,
        string? costCenter,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var entity = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Tenant '{id}' was not found.");

        entity.Name = name;
        entity.PlanSlug = planSlug;
        entity.CostCenter = costCenter;
        entity.UpdatedAt = updatedAt;

        await _db.SaveChangesAsync(cancellationToken);
        return IdentityEntityMapper.ToRecord(entity);
    }
}
