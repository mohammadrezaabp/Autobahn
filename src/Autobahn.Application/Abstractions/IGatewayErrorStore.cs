using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;
/// <summary>Read and administer recorded errors. Backed by the database when one is configured.</summary>
public interface IGatewayErrorStore : IGatewayErrorRecorder
{
    /// <summary>False when errors live only in memory and will not survive a restart.</summary>
    bool IsPersistent { get; }

    Task<GatewayErrorGroupPage> QueryGroupsAsync(GatewayErrorQuery query, CancellationToken cancellationToken = default);
    Task<GatewayErrorPage> QueryAsync(GatewayErrorQuery query, CancellationToken cancellationToken = default);
    Task<GatewayErrorRecord?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<GatewayErrorFacets> GetFacetsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default);
    /// <summary>Removes every record. Returns how many were removed.</summary>
    Task<int> ClearAsync(CancellationToken cancellationToken = default);
}
