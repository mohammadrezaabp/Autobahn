using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autobahn.Application.Fleet;
using Autobahn.Domain.Common;
using Autobahn.Domain.Fleet;

namespace Autobahn.Application.Fleet;

public sealed class FleetControlService(IFleetAgentClient agents, IFleetStateStore store, IGitOpsClient gitOps, IManagedRouteSynchronizer routes)
{
    private readonly SemaphoreSlim _changes = new(1, 1);
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static ServerSpecification Spec(JsonNode value) => value.Deserialize<ServerSpecification>(Json) ?? throw new DomainException("invalid_server", "A server specification is required.");

    public async Task<JsonNode> InventoryAsync(CancellationToken cancellationToken)
    {
        if (gitOps.Enabled) return await gitOps.InventoryAsync(cancellationToken);
        var entries = await store.ListAsync("inventory", cancellationToken);
        var sorted = entries.OrderBy(x => x["name"]!.ToString(), StringComparer.Ordinal).ToArray();
        var revision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', sorted.Select(x => x.ToJsonString())))));
        return new JsonObject { ["commit"] = revision, ["branch"] = "local", ["canPropose"] = true, ["servers"] = new JsonArray(sorted.Select(x => x.DeepClone()).ToArray()) };
    }

    public async Task<JsonNode> FromEveryHostAsync(string path, string key, CancellationToken cancellationToken)
    {
        var rows = await Task.WhenAll(agents.Nodes.Select(async host =>
        {
            try
            {
                var answer = await agents.CallAsync(host.Id, HttpMethod.Get, path, cancellationToken: cancellationToken);
                return (JsonNode)new JsonObject { ["id"] = host.Id, ["ok"] = true, ["error"] = null, [key] = key == "downloads" ? answer["jobs"]?.DeepClone() ?? answer["downloads"]?.DeepClone() ?? new JsonArray() : answer };
            }
            catch (FleetException ex) { return new JsonObject { ["id"] = host.Id, ["ok"] = false, [key] = null, ["error"] = new JsonObject { ["code"] = ex.Code, ["message"] = ex.Message } }; }
        }));
        return new JsonObject { ["time"] = FleetClock.Now, ["nodes"] = new JsonArray(rows) };
    }

    public async Task<JsonNode> ChangeAsync(string host, string path, JsonNode body, string actor, CancellationToken cancellationToken)
    {
        body["requestedBy"] = actor;
        try
        {
            var answer = await agents.CallAsync(host, HttpMethod.Post, path, body, cancellationToken);
            await AuditAsync(actor, path, host, body["repo"]?.ToString() ?? body["path"]?.ToString() ?? "", "ok", "", cancellationToken);
            return answer;
        }
        catch (FleetException ex) { await AuditAsync(actor, path, host, "", ex.Code, ex.Message, CancellationToken.None); throw; }
    }

    public async Task<JsonNode> PlanAsync(string host, JsonNode body, CancellationToken cancellationToken)
    {
        var inventory = await InventoryAsync(cancellationToken);
        var commit = body["commit"]?.ToString();
        if (!string.IsNullOrEmpty(commit) && commit != inventory["commit"]!.ToString())
            throw new FleetException(409, "stale_inventory", "The inventory changed. Review a new plan.");
        var add = body["add"]?.AsArray() ?? [];
        var remove = body["remove"]?.AsArray() ?? [];
        var deployment = Deployment.Draft(host, inventory["commit"]!.ToString(), add.Select(x => Spec(x!)).ToArray(), remove.Select(x => x!.ToString()).ToArray());
        if (!agents.Nodes.Any(x => x.Id == host)) throw new FleetException(404, "unknown_host", "The host is not configured.");
        if (deployment.Add.Length > 0 && (!Uri.TryCreate(agents.Nodes.Single(x => x.Id == host).InferenceAddress, UriKind.Absolute, out var inference) || inference.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(inference.UserInfo)))
            throw new FleetException(409, "inference_address_required", "Configure this host's inference address before deploying a server.");
        var existing = inventory["servers"]!.AsArray();
        var removals = new JsonArray();
        foreach (var name in deployment.Remove)
        {
            var item = existing.SingleOrDefault(x => x?["name"]?.ToString() == name);
            if (item is null || item["host"]?.ToString() != host) throw new FleetException(409, "invalid_removal", $"{name} is not managed on this host.");
            removals.Add(new JsonObject { ["name"] = name, ["path"] = item["path"]?.DeepClone(), ["gpus"] = item["gpus"]?.DeepClone(), ["servedName"] = item["servedName"]?.DeepClone() });
        }
        var plans = new JsonArray();
        foreach (var spec in deployment.Add)
        {
            var conflicts = new List<string>();
            foreach (var other in existing.Where(x => x is not null && !deployment.Remove.Contains(x["name"]!.ToString())))
            {
                if (other!["name"]!.ToString() == spec.Name) conflicts.Add($"Server {spec.Name} already exists.");
                if (other["host"]?.ToString() != host) continue;
                var otherSpec = Spec(other);
                if (otherSpec.Port == spec.Port) conflicts.Add($"Port {spec.Port} is already allocated.");
                if (otherSpec.Gpus.Intersect(spec.Gpus).Any() && !(otherSpec.Shared && spec.Shared)) conflicts.Add("GPU allocation conflicts with the inventory.");
            }
            foreach (var sibling in deployment.Add.Where(x => x.Name != spec.Name))
                if (sibling.Port == spec.Port || (sibling.Gpus.Intersect(spec.Gpus).Any() && !(sibling.Shared && spec.Shared))) conflicts.Add($"Allocation conflicts with {sibling.Name} in this change.");
            JsonNode? plan = null; JsonNode? refusal = null;
            try
            {
                var result = await agents.CallAsync(host, HttpMethod.Post, "/v1/servers", new JsonObject
                { ["spec"] = JsonSerializer.SerializeToNode(spec, Json), ["dryRun"] = true, ["leaving"] = new JsonArray(deployment.Remove.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) }, cancellationToken);
                plan = result["plan"]?.DeepClone() ?? result;
            }
            catch (FleetException ex) { refusal = new JsonObject { ["code"] = ex.Code, ["message"] = ex.Message }; }
            var content = JsonSerializer.Serialize(spec, new JsonSerializerOptions(Json) { WriteIndented = true });
            plans.Add(new JsonObject { ["name"] = spec.Name, ["path"] = $"inventory/servers/{spec.Name}.json", ["content"] = content,
                ["plan"] = plan, ["refusal"] = refusal, ["conflicts"] = new JsonArray(conflicts.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) });
        }
        return new JsonObject { ["commit"] = inventory["commit"]!.DeepClone(), ["removes"] = removals, ["adds"] = plans,
            ["ok"] = plans.All(x => x?["refusal"] is null && x?["conflicts"]?.AsArray().Count == 0) };
    }

    public async Task<JsonNode> ProposeAsync(string host, JsonNode body, string actor, CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken);
        try
        {
            var plan = await PlanAsync(host, body, cancellationToken);
            if (plan["ok"]?.GetValue<bool>() != true) throw new FleetException(409, "plan_refused", "Resolve the plan's conflicts before proposing this change.");
            JsonNode result;
            if (gitOps.Enabled) result = await gitOps.ProposeAsync(host, plan["commit"]!.ToString(), body["add"]!.AsArray(), body["remove"]!.AsArray(), actor, cancellationToken);
            else
            {
                var id = Guid.NewGuid().ToString("N");
                var entry = new JsonObject { ["id"] = id, ["iid"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["url"] = $"/admin/fleet/api/changes/{id}", ["node"] = host,
                    ["actor"] = actor, ["at"] = FleetClock.Now, ["commit"] = plan["commit"]!.DeepClone(), ["add"] = body["add"]!.DeepClone(), ["remove"] = body["remove"]!.DeepClone(), ["status"] = "proposed",
                    ["adds"] = new JsonArray(body["add"]!.AsArray().Select(x => x!["name"]!.DeepClone()).ToArray()), ["removes"] = body["remove"]!.DeepClone(),
                    ["gpus"] = new JsonArray(body["add"]!.AsArray().SelectMany(x => Spec(x!).Gpus).Distinct().Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) };
                await store.WriteAsync("changes", id, entry, cancellationToken);
                result = entry.DeepClone(); result["branch"] = "local"; result["removed"] = result["removes"]!.DeepClone(); result["added"] = result["adds"]!.DeepClone();
            }
            await AuditAsync(actor, "propose", host, "deployment", "ok", "", cancellationToken);
            return result;
        }
        finally { _changes.Release(); }
    }

    public async Task<JsonNode> ApplyAsync(string id, string actor, CancellationToken cancellationToken)
    {
        if (gitOps.Enabled) throw new FleetException(409, "gitops_enabled", "Apply Git-managed inventory through the deployment command after merging.");
        await _changes.WaitAsync(cancellationToken);
        JsonNode? change = null;
        try
        {
            change = await store.ReadAsync("changes", id, cancellationToken) ?? throw new FleetException(404, "unknown_change", "The change request does not exist.");
            if (change["status"]?.ToString() != "proposed") throw new FleetException(409, "invalid_transition", "This change has already been processed.");
            var host = change["node"]!.ToString();
            var plan = await PlanAsync(host, change, cancellationToken);
            if (plan["ok"]?.GetValue<bool>() != true) throw new FleetException(409, "plan_refused", "The host no longer accepts this plan.");
            var inventory = await InventoryAsync(cancellationToken);
            var deployment = Deployment.Draft(host, change["commit"]!.ToString(), change["add"]!.AsArray().Select(x => Spec(x!)).ToArray(), change["remove"]!.AsArray().Select(x => x!.ToString()).ToArray());
            deployment.BeginApply(inventory["commit"]!.ToString());
            change["status"] = "applying"; await store.WriteAsync("changes", id, change, cancellationToken);
            foreach (var name in deployment.Remove)
            {
                var old = inventory["servers"]!.AsArray().Single(x => x!["name"]!.ToString() == name)!;
                await routes.RemoveAsync(host, Spec(old), cancellationToken);
                await agents.CallAsync(host, HttpMethod.Post, $"/v1/servers/{Uri.EscapeDataString(name)}/remove", new JsonObject { ["requestedBy"] = actor }, cancellationToken);
                await store.DeleteAsync("inventory", name, cancellationToken);
            }
            foreach (var spec in deployment.Add)
            {
                await agents.CallAsync(host, HttpMethod.Post, "/v1/servers", new JsonObject { ["spec"] = JsonSerializer.SerializeToNode(spec, Json), ["requestedBy"] = actor }, cancellationToken);
                var entry = JsonSerializer.SerializeToNode(spec, Json)!;
                entry["host"] = host; entry["path"] = $"inventory/servers/{spec.Name}.json"; entry["content"] = entry.ToJsonString(); entry["error"] = ""; entry["hasEnv"] = false; entry["replaces"] = new JsonArray();
                await store.WriteAsync("inventory", spec.Name, entry, cancellationToken);
                await routes.PublishAsync(host, spec, cancellationToken);
            }
            deployment.Complete(); change["status"] = "applied"; await store.WriteAsync("changes", id, change, cancellationToken);
            await AuditAsync(actor, "apply", host, id, "ok", "", cancellationToken);
            return change;
        }
        catch (Exception ex) when (change?["status"]?.ToString() == "applying")
        {
            change!["status"] = "failed"; change["message"] = "The deployment stopped after a host operation failed. Review the host and audit log before retrying.";
            await store.WriteAsync("changes", id, change, CancellationToken.None);
            await AuditAsync(actor, "apply", change["node"]!.ToString(), id, "failed", ex is FleetException failure ? failure.Code : "deployment_failed", CancellationToken.None);
            throw;
        }
        finally { _changes.Release(); }
    }

    public async Task<JsonNode> ChangesAsync(CancellationToken cancellationToken) => gitOps.Enabled ? await gitOps.ChangesAsync(cancellationToken) : new JsonObject
    { ["time"] = FleetClock.Now, ["changes"] = new JsonArray((await store.ListAsync("changes", cancellationToken)).Where(x => x["status"]?.ToString() is "proposed" or "applying" or "failed").Select(x => x.DeepClone()).ToArray()) };

    public async Task<JsonNode> ReconcileAsync(string? expectedCommit, bool dryRun, string actor, CancellationToken cancellationToken)
    {
        if (!gitOps.Enabled) throw new FleetException(409, "gitops_disabled", "Use reviewed change requests for locally managed inventory.");
        await _changes.WaitAsync(cancellationToken);
        try
        {
            var inventory = await InventoryAsync(cancellationToken);
            var revision = inventory["commit"]!.ToString();
            if (!dryRun && (string.IsNullOrEmpty(expectedCommit) || expectedCommit != revision))
                throw new FleetException(409, "stale_inventory", "Plan the current inventory and pass its exact commit to apply it.");
            var desired = inventory["servers"]!.AsArray();
            if (desired.Any(x => !agents.Nodes.Any(n => n.Id == x?["host"]?.ToString())))
                throw new FleetException(409, "unknown_host", "The inventory names a host that is not configured.");
            var plans = new JsonArray();
            var changes = new List<(string Host, ServerSpecification[] Remove, ServerSpecification[] Add, ServerSpecification[] Desired)>();
            foreach (var host in agents.Nodes)
            {
                var current = await agents.CallAsync(host.Id, HttpMethod.Get, "/v1/servers", cancellationToken: cancellationToken);
                var actual = current["servers"]!.AsArray().Select(x => Spec(x!)).ToArray();
                var target = desired.Where(x => x?["host"]?.ToString() == host.Id).Select(x => Spec(x!)).ToArray();
                var policy = current["policy"]!.Deserialize<HostPolicy>(Json)!;
                foreach (var spec in target) policy.EnsureAllowed(spec, target.Where(x => x.Name != spec.Name));
                if (target.Length > 0 && (!Uri.TryCreate(host.InferenceAddress, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
                    throw new FleetException(409, "inference_address_required", "Configure each host's inference address before deployment.");
                static string Canonical(ServerSpecification spec) => JsonSerializer.Serialize(spec, Json);
                var remove = actual.Where(x => !target.Any(t => t.Name == x.Name && Canonical(t) == Canonical(x))).ToArray();
                var add = target.Where(x => !actual.Any(t => t.Name == x.Name && Canonical(t) == Canonical(x))).ToArray();
                foreach (var spec in add)
                {
                    var plan = await agents.CallAsync(host.Id, HttpMethod.Post, "/v1/servers", new JsonObject { ["spec"] = JsonSerializer.SerializeToNode(spec, Json), ["dryRun"] = true,
                        ["leaving"] = new JsonArray(remove.Select(x => (JsonNode?)JsonValue.Create(x.Name)).ToArray()) }, cancellationToken);
                    plans.Add(new JsonObject { ["host"] = host.Id, ["name"] = spec.Name, ["plan"] = plan });
                }
                foreach (var spec in remove) plans.Add(new JsonObject { ["host"] = host.Id, ["name"] = spec.Name, ["plan"] = new JsonObject { ["action"] = "remove" } });
                changes.Add((host.Id, remove, add, target));
            }
            if (!dryRun)
            {
                // Recheck the branch after preflight, before the first host mutation.
                if ((await InventoryAsync(cancellationToken))["commit"]!.ToString() != revision)
                    throw new FleetException(409, "stale_inventory", "The inventory changed during preflight.");
                foreach (var change in changes)
                {
                    foreach (var spec in change.Remove)
                    {
                        await routes.RemoveAsync(change.Host, spec, cancellationToken);
                        await agents.CallAsync(change.Host, HttpMethod.Post, $"/v1/servers/{Uri.EscapeDataString(spec.Name)}/remove", new JsonObject { ["requestedBy"] = actor }, cancellationToken);
                    }
                    foreach (var spec in change.Add)
                        await agents.CallAsync(change.Host, HttpMethod.Post, "/v1/servers", new JsonObject { ["spec"] = JsonSerializer.SerializeToNode(spec, Json), ["requestedBy"] = actor }, cancellationToken);
                    foreach (var spec in change.Desired) await routes.PublishAsync(change.Host, spec, cancellationToken);
                }
                await AuditAsync(actor, "inventory.apply", "all", revision, "ok", "", cancellationToken);
            }
            return new JsonObject { ["commit"] = revision, ["dryRun"] = dryRun, ["plans"] = plans };
        }
        catch (Exception) when (!dryRun)
        { await AuditAsync(actor, "inventory.apply", "all", expectedCommit ?? "", "failed", "Review host state before retrying a partial deployment.", CancellationToken.None); throw; }
        finally { _changes.Release(); }
    }

    public Task AuditAsync(string actor, string action, string node, string target, string outcome, string detail, CancellationToken cancellationToken) =>
        store.WriteAsync("audit", Guid.NewGuid().ToString("N"), new JsonObject { ["id"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["at"] = FleetClock.Now, ["actor"] = actor, ["action"] = action, ["node"] = node, ["target"] = target, ["outcome"] = outcome, ["detail"] = detail }, cancellationToken);
}
