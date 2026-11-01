using System.Text.Json.Nodes;

namespace Autobahn.Application.Fleet;

public interface IFleetStateStore
{
    Task<JsonNode?> ReadAsync(string collection, string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JsonNode>> ListAsync(string collection, CancellationToken cancellationToken = default);
    Task WriteAsync(string collection, string id, JsonNode value, CancellationToken cancellationToken = default);
    Task DeleteAsync(string collection, string id, CancellationToken cancellationToken = default);
}
