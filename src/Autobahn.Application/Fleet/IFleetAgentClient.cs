using System.Text.Json.Nodes;

namespace Autobahn.Application.Fleet;

public interface IFleetAgentClient
{
    IReadOnlyList<FleetNode> Nodes { get; }
    Task<JsonNode> CallAsync(string node, HttpMethod method, string path, JsonNode? body = null, CancellationToken cancellationToken = default);
}
