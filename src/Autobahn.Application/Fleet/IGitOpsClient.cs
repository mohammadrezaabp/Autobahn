using System.Text.Json.Nodes;

namespace Autobahn.Application.Fleet;

public interface IGitOpsClient
{
    bool Enabled { get; }
    Task<JsonNode> InventoryAsync(CancellationToken cancellationToken = default);
    Task<JsonNode> ProposeAsync(string host, string commit, JsonArray add, JsonArray remove, string actor, CancellationToken cancellationToken = default);
    Task<JsonNode> ChangesAsync(CancellationToken cancellationToken = default);
}
