using System.Text.Json.Nodes;

namespace Autobahn.Infrastructure.Agent;

public static class AgentRuntimeDiscovery
{
    public static void Attach(JsonNode gpuReport, JsonArray containers, IReadOnlyList<JsonNode> specifications)
    {
        foreach (var gpu in gpuReport["gpus"]?.AsArray() ?? [])
        {
            if (gpu is null) continue;
            var index = gpu["index"]!.GetValue<int>();
            foreach (var spec in specifications.Where(s => s["gpus"]!.AsArray().Any(g => g?.GetValue<int>() == index)))
            {
                var name = spec["name"]!.ToString();
                var container = containers.FirstOrDefault(c => c?["Config"]?["Labels"]?["autobahn.server"]?.ToString() == name && c["State"]?["Running"]?.GetValue<bool>() == true);
                if (container is null) continue;
                var initPid = container["State"]?["Pid"]?.GetValue<int>() ?? 0;
                var matched = false;
                foreach (var process in gpu["processes"]!.AsArray())
                {
                    if (process is null || !DescendsFrom(process["pid"]!.GetValue<int>(), initPid)) continue;
                    process["kind"] = "model"; process["servedName"] = spec["servedName"]!.DeepClone(); process["serverPid"] = initPid; process["containerId"] = container["Id"]!.DeepClone(); matched = true;
                }
                if (!matched) gpu["processes"]!.AsArray().Add(new JsonObject { ["pid"] = initPid, ["memoryMiB"] = null, ["kind"] = "model", ["servedName"] = spec["servedName"]!.DeepClone(),
                    ["serverPid"] = initPid, ["user"] = "", ["containerId"] = container["Id"]!.DeepClone(), ["command"] = "", ["source"] = "assignment" });
            }
        }
    }

    private static bool DescendsFrom(int pid, int ancestor)
    {
        if (pid <= 0 || ancestor <= 0) return false;
        for (var depth = 0; depth < 64 && pid > 1; depth++)
        {
            if (pid == ancestor) return true;
            if (!OperatingSystem.IsLinux()) return false;
            try
            {
                var parent = File.ReadLines($"/proc/{pid}/status").FirstOrDefault(x => x.StartsWith("PPid:", StringComparison.Ordinal));
                if (parent is null || !int.TryParse(parent.AsSpan(5).Trim(), out pid)) return false;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        return false;
    }
}
