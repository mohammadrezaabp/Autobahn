using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;
/// <summary>
/// Durable storage for error records. Implemented over the database and resolved per-scope, so
/// nothing on the request path ever holds a DbContext.
/// </summary>
public interface IGatewayErrorArchive
{
    Task AppendBatchAsync(IReadOnlyList<GatewayErrorRecord> batch, CancellationToken cancellationToken = default);
    Task<GatewayErrorPage> QueryAsync(GatewayErrorQuery query, CancellationToken cancellationToken = default);
    Task<GatewayErrorGroupPage> QueryGroupsAsync(GatewayErrorQuery query, CancellationToken cancellationToken = default);
    Task<GatewayErrorRecord?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<GatewayErrorFacets> GetFacetsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default);
    /// <summary>
    /// Whether any archived error names <paramref name = "apiKeyId"/>. A key whose only trace is a
    /// failed request still produced an auditable record that must not be orphaned by deletion.
    /// </summary>
    Task<bool> HasEventsForKeyAsync(string apiKeyId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    /// <summary>The subset of <paramref name = "apiKeyIds"/> that at least one archived error names.</summary>
    Task<IReadOnlySet<string>> FindKeysWithEventsAsync(IReadOnlyCollection<string> apiKeyIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Drops records older than <paramref name = "olderThan"/>, then trims the oldest surviving rows
    /// down to <paramref name = "maxRows"/>. Returns the total removed.
    /// </summary>
    Task<int> PruneAsync(DateTimeOffset olderThan, int maxRows, CancellationToken cancellationToken = default);
}
