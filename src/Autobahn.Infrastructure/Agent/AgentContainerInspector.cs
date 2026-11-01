using System.Text.Json.Nodes;
using Autobahn.Application.Fleet;
using Microsoft.Extensions.Options;

namespace Autobahn.Infrastructure.Agent;

public sealed class AgentContainerInspector(IHostProcessRunner runner, IOptions<AgentOptions> options)
{
    public async Task<JsonArray> ReadAsync(CancellationToken cancellationToken)
    {
        var ids = (await runner.RunAsync(options.Value.Docker, ["ps", "-aq"], cancellationToken)).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length == 0) return [];
        return JsonNode.Parse(await runner.RunAsync(options.Value.Docker, new[] { "inspect" }.Concat(ids), cancellationToken))!.AsArray();
    }

    public async Task EnsureNotMountedAsync(string directory, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var container in await ReadAsync(cancellationToken))
        {
            if (container?["State"]?["Running"]?.GetValue<bool>() != true) continue;
            foreach (var mount in container["Mounts"]?.AsArray() ?? [])
            {
                var source = mount?["Source"]?.ToString(); if (string.IsNullOrWhiteSpace(source)) continue;
                source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (source.Equals(full, comparison) || source.StartsWith(full + Path.DirectorySeparatorChar, comparison) || full.StartsWith(source + Path.DirectorySeparatorChar, comparison))
                    throw new FleetException(409, "model_in_use", "A running container mounts this model directory. Stop it before deleting the model.");
            }
        }
    }

    public async Task<JsonNode> ImportAsync(CancellationToken cancellationToken)
    {
        var candidates = new JsonArray();
        foreach (var container in await ReadAsync(cancellationToken))
        {
            if (container?["Config"]?["Labels"]?["autobahn.managed"]?.ToString() == "true") continue;
            var args = (container?["Config"]?["Cmd"]?.AsArray() ?? []).Select(x => x!.ToString()).ToArray();
            string Flag(string flag, string fallback = "")
            { var index = Array.IndexOf(args, flag); return index >= 0 && index + 1 < args.Length ? args[index + 1] : args.FirstOrDefault(x => x.StartsWith(flag + "=", StringComparison.Ordinal))?[(flag.Length + 1)..] ?? fallback; }
            var model = Flag("--model");
            if (model.Length == 0 && args.Length > 1 && args[0] == "serve") model = args[1];
            if (model.Length == 0) continue;
            var binding = container?["HostConfig"]?["PortBindings"]?.AsObject().FirstOrDefault(x => x.Key.StartsWith(Flag("--port", "8000") + "/", StringComparison.Ordinal)).Value?.AsArray().FirstOrDefault();
            var gpus = new JsonArray();
            foreach (var device in container?["HostConfig"]?["DeviceRequests"]?.AsArray() ?? [])
                foreach (var id in device?["DeviceIDs"]?.AsArray() ?? []) if (int.TryParse(id?.ToString(), out var index)) gpus.Add(index);
            var mounted = container?["Mounts"]?.AsArray().FirstOrDefault(m => m?["Destination"]?.ToString() == model);
            var hostPath = mounted?["Source"]?.ToString();
            var root = Path.GetFullPath(options.Value.ModelDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var relative = hostPath is not null && Path.GetFullPath(hostPath).StartsWith(root, StringComparison.Ordinal) ? Path.GetRelativePath(root, hostPath).Replace('\\', '/') : model;
            var name = (container?["Name"]?.ToString().TrimStart('/') ?? "imported-model").Replace('_', '-').ToLowerInvariant();
            candidates.Add(new JsonObject { ["name"] = name, ["host"] = options.Value.Name, ["model"] = relative, ["revision"] = "main", ["gpus"] = gpus,
                ["port"] = int.TryParse(binding?["HostPort"]?.ToString(), out var port) ? port : null, ["image"] = container?["Config"]?["Image"]?.DeepClone(),
                ["servedName"] = Flag("--served-model-name", Path.GetFileName(model)), ["apiKey"] = true, ["args"] = new JsonArray(), ["shared"] = false,
                ["warnings"] = new JsonArray("Review model paths, runtime arguments, GPU allocation and port before proposing this server. The existing container is not changed.") });
        }
        return new JsonObject { ["servers"] = candidates };
    }
}
