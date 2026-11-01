using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Autobahn.Application.Fleet;
using Microsoft.Extensions.Options;

namespace Autobahn.Infrastructure.Fleet;

public sealed class GitLabGitOpsClient(IHttpClientFactory clients, IOptions<FleetOptions> options) : IGitOpsClient
{
    private GitOpsOptions Settings => options.Value.GitOps;
    public bool Enabled => !string.IsNullOrWhiteSpace(Settings.Url) && !string.IsNullOrWhiteSpace(Settings.Project) && !string.IsNullOrWhiteSpace(Settings.Token);
    private string ProjectPath => "/projects/" + Uri.EscapeDataString(Settings.Project);

    private async Task<JsonNode> CallAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(Settings.Url, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new FleetException(503, "gitops_configuration", "Configure a valid Git service URL.");
        using var request = new HttpRequestMessage(method, Settings.Url.TrimEnd('/') + "/api/v4" + path);
        request.Headers.Add("PRIVATE-TOKEN", Settings.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await clients.CreateClient("fleet-gitops").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new FleetException(502, "gitops_error", $"The Git service returned {(int)response.StatusCode}.");
            return await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken) ?? new JsonObject();
        }
        catch (HttpRequestException) { throw new FleetException(502, "gitops_unavailable", "The Git service is not reachable."); }
    }

    public async Task<JsonNode> InventoryAsync(CancellationToken cancellationToken = default)
    {
        var branch = Uri.EscapeDataString(Settings.Branch);
        var commit = await CallAsync(HttpMethod.Get, $"{ProjectPath}/repository/commits/{branch}", null, cancellationToken);
        var sha = commit["id"]!.ToString();
        var tree = new JsonArray();
        for (var page = 1; page <= 100; page++)
        {
            var batch = (await CallAsync(HttpMethod.Get, $"{ProjectPath}/repository/tree?path=inventory%2Fservers&ref={sha}&per_page=100&page={page}", null, cancellationToken)).AsArray();
            foreach (var item in batch) tree.Add(item!.DeepClone());
            if (batch.Count < 100) break;
        }
        var servers = new JsonArray();
        foreach (var item in tree.Where(x => x?["type"]?.ToString() == "blob" && x["name"]!.ToString().EndsWith(".json", StringComparison.Ordinal)))
        {
            var path = item!["path"]!.ToString();
            var file = await CallAsync(HttpMethod.Get, $"{ProjectPath}/repository/files/{Uri.EscapeDataString(path)}?ref={sha}", null, cancellationToken);
            var content = Encoding.UTF8.GetString(Convert.FromBase64String(file["content"]!.ToString()));
            var spec = JsonNode.Parse(content)!;
            spec["path"] = path; spec["content"] = content; spec["error"] = ""; spec["hasEnv"] = false; spec["replaces"] ??= new JsonArray();
            servers.Add(spec);
        }
        return new JsonObject { ["commit"] = sha, ["branch"] = Settings.Branch, ["servers"] = servers, ["canPropose"] = true };
    }

    public async Task<JsonNode> ProposeAsync(string host, string commit, JsonArray add, JsonArray remove, string actor, CancellationToken cancellationToken = default)
    {
        var inventory = await InventoryAsync(cancellationToken);
        if (inventory["commit"]!.ToString() != commit) throw new FleetException(409, "stale_inventory", "The inventory changed. Review a new plan.");
        var branch = "autobahn/fleet-" + Guid.NewGuid().ToString("N");
        var actions = new JsonArray();
        foreach (var item in remove) actions.Add(new JsonObject { ["action"] = "delete", ["file_path"] = $"inventory/servers/{item}.json" });
        foreach (var item in add)
        {
            var spec = item!.DeepClone(); spec["host"] = host;
            actions.Add(new JsonObject { ["action"] = "create", ["file_path"] = $"inventory/servers/{spec["name"]}.json", ["content"] = spec.ToJsonString() });
        }
        await CallAsync(HttpMethod.Post, ProjectPath + "/repository/commits", new JsonObject { ["branch"] = branch, ["start_sha"] = commit, ["commit_message"] = $"Update fleet on {host}", ["actions"] = actions }, cancellationToken);
        var mr = await CallAsync(HttpMethod.Post, ProjectPath + "/merge_requests", new JsonObject
        { ["source_branch"] = branch, ["target_branch"] = Settings.Branch, ["title"] = $"Update fleet on {host}", ["description"] = $"Requested by {actor} through Autobahn.", ["remove_source_branch"] = true, ["labels"] = "autobahn-fleet" }, cancellationToken);
        return new JsonObject { ["url"] = mr["web_url"]!.DeepClone(), ["iid"] = mr["iid"]!.DeepClone(), ["branch"] = branch,
            ["removed"] = remove.DeepClone(), ["added"] = new JsonArray(add.Select(x => x!["name"]!.DeepClone()).ToArray()),
            ["gpus"] = new JsonArray(add.SelectMany(x => FleetControlService.Spec(x!).Gpus).Distinct().Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) };
    }

    public async Task<JsonNode> ChangesAsync(CancellationToken cancellationToken = default)
    {
        var requests = await CallAsync(HttpMethod.Get, ProjectPath + "/merge_requests?state=opened&labels=autobahn-fleet&per_page=100", null, cancellationToken);
        return new JsonObject { ["time"] = FleetPoller.Now, ["changes"] = new JsonArray(requests.AsArray().Select(x => (JsonNode?)new JsonObject
        { ["iid"] = x!["iid"]!.DeepClone(), ["url"] = x["web_url"]!.DeepClone(), ["node"] = x["title"]!.ToString().Replace("Update fleet on ", ""), ["actor"] = x["author"]?["username"]?.DeepClone(), ["at"] = DateTimeOffset.Parse(x["created_at"]!.ToString()).ToUnixTimeSeconds(), ["gpus"] = new JsonArray(), ["adds"] = new JsonArray(), ["removes"] = new JsonArray() }).ToArray()) };
    }
}
