using Microsoft.EntityFrameworkCore;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Persistence.Mapping;

namespace Autobahn.Persistence.Repositories;

public sealed class ApiKeyModelGrantRepository : IApiKeyModelGrantRepository
{
    private readonly GatewayDbContext _db;

    public ApiKeyModelGrantRepository(GatewayDbContext db) => _db = db;

    public async Task<IReadOnlyList<ApiKeyModelGrantRecord>> ListByApiKeyAsync(
        Guid apiKeyId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _db.ApiKeyModelGrants
            .AsNoTracking()
            .Where(g => g.ApiKeyId == apiKeyId)
            .OrderBy(g => g.ModelPattern)
            .ToListAsync(cancellationToken);

        return entities.Select(IdentityEntityMapper.ToRecord).ToList();
    }

    public Task ReplaceForApiKeyAsync(
        Guid apiKeyId,
        IReadOnlyList<string> modelPatterns,
        CancellationToken cancellationToken = default) =>
        ReplaceForApiKeyAsync(
            apiKeyId,
            modelPatterns.Select(p => (p, GrantEffect.Allow)).ToList(),
            cancellationToken);

    public async Task ReplaceForApiKeyAsync(
        Guid apiKeyId,
        IReadOnlyList<(string ModelPattern, GrantEffect Effect)> grants,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.ApiKeyModelGrants
            .Where(g => g.ApiKeyId == apiKeyId)
            .ToListAsync(cancellationToken);

        _db.ApiKeyModelGrants.RemoveRange(existing);

        // One row per pattern, which is what the unique index holds a key to. A pattern given as
        // both allow and deny is kept as the deny.
        var byPattern = grants
            .Where(g => !string.IsNullOrWhiteSpace(g.ModelPattern))
            .GroupBy(g => g.ModelPattern.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in byPattern)
        {
            _db.ApiKeyModelGrants.Add(new Entities.ApiKeyModelGrantEntity
            {
                Id = Guid.NewGuid(),
                ApiKeyId = apiKeyId,
                ModelPattern = group.Key,
                Effect = group.Any(g => g.Effect == GrantEffect.Deny) ? GrantEffect.Deny : GrantEffect.Allow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
