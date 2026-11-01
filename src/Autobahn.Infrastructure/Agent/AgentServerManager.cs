using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autobahn.Application.Fleet;
using Autobahn.Domain.Fleet;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Autobahn.Infrastructure.Agent;

public sealed class AgentServerManager(IHostProcessRunner runner, IOptions<AgentOptions> options, IFleetStateStore state, AgentModelStore models, IDataProtectionProvider protection, IHttpClientFactory clients, AgentMutationGate mutations)
{
    private readonly SemaphoreSlim _gate = mutations.Semaphore;
    private readonly IDataProtector _protector = protection.CreateProtector("Autobahn.Agent.ServerCredentials.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Task<string> DockerAsync(IEnumerable<string> args, CancellationToken cancellationToken, int timeout = 30) => runner.RunAsync(options.Value.Docker, args, cancellationToken, timeout);
    private static string ContainerName(string name) => "autobahn-" + name;

    public async Task<JsonNode> ListAsync(CancellationToken cancellationToken)
    {
        var specs = await state.ListAsync("servers", cancellationToken);
        var containers = await ContainersAsync(cancellationToken);
        var rows = new JsonArray();
        foreach (var entry in specs)
        {
            var spec = entry.Deserialize<ServerSpecification>(Json)!;
            var container = containers.FirstOrDefault(x => x?["Name"]?.ToString().TrimStart('/') == ContainerName(spec.Name));
            var row = entry.DeepClone(); row["state"] = container?["State"]?["Status"]?.ToString() ?? "missing";
            rows.Add(row);
        }
        return new JsonObject { ["policy"] = JsonSerializer.SerializeToNode(options.Value.ServerPolicy, Json), ["servers"] = rows };
    }

    public async Task<JsonArray> ContainersAsync(CancellationToken cancellationToken)
    {
        var ids = (await DockerAsync(["ps", "-aq"], cancellationToken)).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length == 0) return [];
        return JsonNode.Parse(await DockerAsync(new[] { "inspect" }.Concat(ids), cancellationToken))!.AsArray();
    }

    public async Task<JsonNode> ApplyAsync(ServerSpecification spec, bool dryRun, string[] leaving, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await state.ListAsync("servers", cancellationToken);
            // Only a plan may discount servers that a reviewed change intends to remove.
            var current = entries.Select(x => x.Deserialize<ServerSpecification>(Json)!).Where(x => !dryRun || !leaving.Contains(x.Name)).ToArray();
            options.Value.ServerPolicy.EnsureAllowed(spec, current);
            var path = await models.ResolveRepositoryAsync(spec.Model, spec.Revision, cancellationToken);
            var containers = await ContainersAsync(cancellationToken);
            foreach (var foreign in containers.Where(x => x?["State"]?["Running"]?.GetValue<bool>() == true))
            {
                var labels = foreign?["Config"]?["Labels"];
                var managed = labels?["autobahn.managed"]?.ToString() == "true";
                var name = labels?["autobahn.server"]?.ToString() ?? "";
                if (managed && (name == spec.Name || (dryRun && leaving.Contains(name)))) continue;
                var bindings = foreign?["HostConfig"]?["PortBindings"]?.AsObject();
                if (bindings is not null && bindings.Any(x => x.Value?.AsArray().Any(b => b?["HostPort"]?.ToString() == spec.Port.ToString()) == true))
                    throw new FleetException(409, "port_busy", "The requested port is used by a running container.");
                var devices = foreign?["HostConfig"]?["DeviceRequests"]?.AsArray();
                if (devices is not null && devices.Any(d => d?["Count"]?.GetValue<int>() == -1 || d?["DeviceIDs"]?.AsArray().Any(g => spec.Gpus.Any(i => i.ToString() == g?.ToString())) == true))
                {
                    var sibling = current.FirstOrDefault(x => x.Name == name);
                    if (!(managed && spec.Shared && sibling?.Shared == true)) throw new FleetException(409, "gpu_busy", "A selected GPU is assigned to another running container.");
                }
            }
            bool imagePresent;
            try { await DockerAsync(["image", "inspect", spec.Image], cancellationToken); imagePresent = true; }
            catch (FleetException) { imagePresent = false; }
            var old = entries.SingleOrDefault(x => x["name"]?.ToString() == spec.Name);
            var plan = new JsonObject { ["action"] = old is null ? "create" : "replace", ["image"] = spec.Image, ["imagePresent"] = imagePresent,
                ["warnings"] = imagePresent ? new JsonArray() : new JsonArray("The image must be downloaded before the server starts.") };
            if (dryRun) return plan;
            var existing = containers.SingleOrDefault(x => x?["Name"]?.ToString().TrimStart('/') == ContainerName(spec.Name));
            if (existing is not null)
            {
                if (existing["Config"]?["Labels"]?["autobahn.managed"]?.ToString() != "true") throw new FleetException(409, "unmanaged_container", "A container with this name is not managed by Autobahn.");
                await DockerAsync(["rm", "-f", ContainerName(spec.Name)], cancellationToken, 120);
            }
            var secret = spec.ApiKey ? Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)) : "";
            if (secret.Length > 0) await state.WriteAsync("server-keys", spec.Name, JsonValue.Create(_protector.Protect(secret))!, cancellationToken);
            var envFile = Path.Combine(Path.GetFullPath(options.Value.DataDirectory), Guid.NewGuid().ToString("N") + ".env");
            Directory.CreateDirectory(Path.GetDirectoryName(envFile)!);
            await File.WriteAllTextAsync(envFile, $"VLLM_API_KEY={secret}\nHF_HUB_OFFLINE=1\nTRANSFORMERS_OFFLINE=1\nHF_HOME=/tmp/huggingface\n", cancellationToken);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(envFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            try
            {
                var args = new List<string> { "run", "-d", "--name", ContainerName(spec.Name), "--restart", "unless-stopped", "--label", "autobahn.managed=true", "--label", "autobahn.server=" + spec.Name,
                    "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--shm-size", "2g", "--gpus", "\"device=" + string.Join(',', spec.Gpus) + "\"",
                    "--publish", $"{spec.Port}:8000", "--volume", path + ":/models:ro", "--env-file", envFile, "--entrypoint", "vllm", spec.Image,
                    "serve", "/models", "--served-model-name", spec.ServedName, "--host", "0.0.0.0", "--port", "8000" };
                args.AddRange(spec.Args);
                await state.WriteAsync("servers", spec.Name, JsonSerializer.SerializeToNode(spec, Json)!, cancellationToken);
                await DockerAsync(args, cancellationToken, 1800);
            }
            finally { File.Delete(envFile); }
            await state.WriteAsync("servers", spec.Name, JsonSerializer.SerializeToNode(spec, Json)!, cancellationToken);
            await WaitReadyAsync(spec, secret, cancellationToken);
            return new JsonObject { ["name"] = spec.Name, ["state"] = "running", ["plan"] = plan };
        }
        finally { _gate.Release(); }
    }

    private async Task WaitReadyAsync(ServerSpecification spec, string key, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.ServerStartupTimeoutSeconds, 1, 3600)));
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{spec.Port}/v1/models");
                    if (key.Length > 0) request.Headers.Authorization = new("Bearer", key);
                    using var response = await clients.CreateClient("agent-probes").SendAsync(request, timeout.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                        if (body?["data"]?.AsArray().Any(m => m?["id"]?.ToString() == spec.ServedName) == true) return;
                    }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!timeout.IsCancellationRequested) { }
                var containers = await ContainersAsync(timeout.Token);
                var container = containers.FirstOrDefault(x => x?["Name"]?.ToString().TrimStart('/') == ContainerName(spec.Name));
                if (container is null || container["State"]?["Running"]?.GetValue<bool>() != true)
                    throw new FleetException(502, "server_exited", "The model server exited before becoming ready. Review its container logs.");
                await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new FleetException(504, "server_startup_timeout", "The model server did not become ready before the startup timeout. Its container is retained for inspection."); }
    }

    public async Task<JsonNode> StopAsync(string name, bool remove, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _ = await state.ReadAsync("servers", name, cancellationToken) ?? throw new FleetException(404, "unknown_server", "The server is not managed by Autobahn.");
            var containers = await ContainersAsync(cancellationToken);
            var container = containers.FirstOrDefault(x => x?["Name"]?.ToString().TrimStart('/') == ContainerName(name));
            if (container is not null)
            {
                if (container["Config"]?["Labels"]?["autobahn.managed"]?.ToString() != "true") throw new FleetException(409, "unmanaged_container", "This container is not managed by Autobahn.");
                await DockerAsync(remove ? ["rm", "-f", ContainerName(name)] : ["stop", "--time", "60", ContainerName(name)], cancellationToken, 120);
            }
            if (remove) { await state.DeleteAsync("servers", name, cancellationToken); await state.DeleteAsync("server-keys", name, cancellationToken); }
            return new JsonObject { ["name"] = name, ["state"] = remove ? "removed" : "stopped" };
        }
        finally { _gate.Release(); }
    }

    public async Task<string> KeyAsync(string name, CancellationToken cancellationToken)
    {
        var value = await state.ReadAsync("server-keys", name, cancellationToken);
        return value is null ? "" : _protector.Unprotect(value.GetValue<string>());
    }

    public async Task<JsonNode> ServingAsync(string? servedName, CancellationToken cancellationToken)
    {
        var servers = new JsonArray();
        foreach (var value in await state.ListAsync("servers", cancellationToken))
        {
            var spec = value.Deserialize<ServerSpecification>(Json)!;
            if (!string.IsNullOrWhiteSpace(servedName) && spec.ServedName != servedName) continue;
            var row = new JsonObject { ["name"] = spec.Name, ["servedName"] = spec.ServedName, ["port"] = spec.Port, ["ok"] = false, ["maxContextLength"] = null };
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{spec.Port}/v1/models");
                var key = await KeyAsync(spec.Name, cancellationToken); if (key.Length > 0) request.Headers.Authorization = new("Bearer", key);
                using var response = await clients.CreateClient("agent-probes").SendAsync(request, cancellationToken);
                row["ok"] = response.IsSuccessStatusCode;
                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadAsStringAsync(cancellationToken);
                    var model = JsonNode.Parse(data)?["data"]?.AsArray().FirstOrDefault();
                    row["maxContextLength"] = model?["max_model_len"]?.DeepClone();
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { row["error"] = "The model server did not answer."; }
            servers.Add(row);
        }
        return new JsonObject { ["name"] = options.Value.Name, ["time"] = Fleet.FleetPoller.Now, ["servers"] = servers };
    }
}
