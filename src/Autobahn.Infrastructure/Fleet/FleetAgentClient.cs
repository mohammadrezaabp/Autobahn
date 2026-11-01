using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json;
using Autobahn.Application.Fleet;
using Microsoft.Extensions.Options;

namespace Autobahn.Infrastructure.Fleet;

public sealed class FleetAgentClient(IHttpClientFactory clients, IOptions<FleetOptions> options) : IFleetAgentClient
{
    public IReadOnlyList<FleetNode> Nodes => options.Value.Nodes;

    public async Task<JsonNode> CallAsync(string node, HttpMethod method, string path, JsonNode? body = null, CancellationToken cancellationToken = default)
    {
        var host = Nodes.SingleOrDefault(x => x.Id == node) ?? throw new FleetException(404, "unknown_host", "The host is not configured.");
        if (!path.StartsWith("/v1/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            throw new FleetException(400, "invalid_path", "The agent path is invalid.");
        using var request = new HttpRequestMessage(method, host.Address.TrimEnd('/') + path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(method == HttpMethod.Post && path == "/v1/servers" && body?["dryRun"]?.GetValue<bool>() != true ? TimeSpan.FromMinutes(65) : TimeSpan.FromSeconds(30));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await clients.CreateClient("fleet-agents").SendAsync(request, timeout.Token);
            var result = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken) ?? new JsonObject();
            if (!response.IsSuccessStatusCode)
                throw new FleetException((int)response.StatusCode, result["code"]?.GetValue<string>() ?? "agent_error", result["message"]?.GetValue<string>() ?? "The agent refused the request.");
            return result;
        }
        catch (HttpRequestException) { throw new FleetException(502, "agent_unavailable", "The host agent is not reachable."); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        { throw new FleetException(502, "invalid_agent_response", "The host agent returned an invalid response."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new FleetException(504, "agent_timeout", "The host agent did not respond in time."); }
    }
}
