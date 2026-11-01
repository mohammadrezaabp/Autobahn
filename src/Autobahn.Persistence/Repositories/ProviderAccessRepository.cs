using Microsoft.EntityFrameworkCore;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Repositories;

public sealed class ProviderAccessRepository : IProviderAccessRepository
{
    private readonly GatewayDbContext _db;

    public ProviderAccessRepository(GatewayDbContext db) => _db = db;

    /// <remarks>
    /// A key's switch counts only while the key is still in the tenant that set it. A key moved to
    /// another tenant starts closed there: the tenant that opened it is not the one now paying.
    /// </remarks>
    public async Task<IReadOnlyList<ProviderAccessRecord>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _db.ProviderAccess
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Where(p => p.ApiKeyId == null || _db.ApiKeys.Any(k => k.Id == p.ApiKeyId && k.TenantId == tenantId))
            .ToListAsync(cancellationToken);

        return entities
            .Select(ToRecord)
            .ToList();
    }

    /// <remarks>Keys that have left their tenant are dropped, as in <see cref="ListByTenantAsync"/>.</remarks>
    public async Task<IReadOnlyList<ProviderAccessRecord>> ListByProviderAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        var entities = await _db.ProviderAccess
            .AsNoTracking()
            .Where(p => p.Provider == provider)
            .Where(p => p.ApiKeyId == null || _db.ApiKeys.Any(k => k.Id == p.ApiKeyId && k.TenantId == p.TenantId))
            .ToListAsync(cancellationToken);

        return entities.Select(ToRecord).ToList();
    }

    public async Task<decimal?> SetCapAsync(
        Guid tenantId,
        Guid? apiKeyId,
        string provider,
        decimal? monthlyCapUsd,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.ProviderAccess
            .Where(p => p.TenantId == tenantId && p.ApiKeyId == apiKeyId && p.Provider == provider)
            .ToListAsync(cancellationToken);

        var previous = rows.Count > 0 ? rows[0].MonthlyCapUsd : null;
        var now = DateTimeOffset.UtcNow;

        if (rows.Count == 0)
        {
            _db.ProviderAccess.Add(new ProviderAccessEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ApiKeyId = apiKeyId,
                Provider = provider,
                IsOpen = false,
                MonthlyCapUsd = monthlyCapUsd,
                UpdatedAt = now,
            });
        }
        else
        {
            rows[0].MonthlyCapUsd = monthlyCapUsd;
            rows[0].UpdatedAt = now;
            _db.ProviderAccess.RemoveRange(rows.Skip(1));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return previous;
    }

    private static ProviderAccessRecord ToRecord(ProviderAccessEntity p) =>
        new(p.Id, p.TenantId, p.ApiKeyId, p.Provider, p.IsOpen, p.MonthlyCapUsd);

    public async Task<bool> SetAsync(
        Guid tenantId,
        Guid? apiKeyId,
        string provider,
        bool isOpen,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.ProviderAccess
            .Where(p => p.TenantId == tenantId && p.ApiKeyId == apiKeyId && p.Provider == provider)
            .ToListAsync(cancellationToken);

        var previous = rows.Count > 0 && rows[0].IsOpen;
        var now = DateTimeOffset.UtcNow;

        if (rows.Count == 0)
        {
            _db.ProviderAccess.Add(new ProviderAccessEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ApiKeyId = apiKeyId,
                Provider = provider,
                IsOpen = isOpen,
                UpdatedAt = now,
            });
        }
        else
        {
            rows[0].IsOpen = isOpen;
            rows[0].UpdatedAt = now;

            // Two writers racing the first write can each have added a row; keep one.
            _db.ProviderAccess.RemoveRange(rows.Skip(1));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return previous;
    }
}
