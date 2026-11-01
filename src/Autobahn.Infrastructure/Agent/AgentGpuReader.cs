using System.Globalization;
using System.Text.Json.Nodes;
using Autobahn.Application.Fleet;
using Microsoft.Extensions.Options;

namespace Autobahn.Infrastructure.Agent;

public sealed class AgentGpuReader(IHostProcessRunner runner, IOptions<AgentOptions> options)
{
    public async Task<JsonNode> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var csv = await runner.RunAsync(options.Value.NvidiaSmi,
                ["--query-gpu=index,uuid,name,memory.used,memory.total,utilization.gpu,temperature.gpu,power.draw,power.limit,ecc.errors.uncorrected.volatile.total,driver_version", "--format=csv,noheader,nounits"], cancellationToken);
            var gpus = new JsonArray(); string driver = "";
            foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var cells = line.Trim().Split(',').Select(x => x.Trim()).ToArray(); if (cells.Length < 11) continue;
                double? Number(int i) => double.TryParse(cells[i], CultureInfo.InvariantCulture, out var n) ? n : null;
                driver = cells[10];
                gpus.Add(new JsonObject { ["index"] = (int)(Number(0) ?? 0), ["uuid"] = cells[1], ["name"] = cells[2], ["memoryUsedMiB"] = Number(3), ["memoryTotalMiB"] = Number(4),
                    ["utilizationPct"] = Number(5), ["temperatureC"] = Number(6), ["powerW"] = Number(7), ["powerLimitW"] = Number(8), ["eccUncorrected"] = Number(9), ["error"] = null, ["processes"] = new JsonArray() });
            }
            try
            {
                var processes = await runner.RunAsync(options.Value.NvidiaSmi, ["--query-compute-apps=gpu_uuid,pid,used_memory", "--format=csv,noheader,nounits"], cancellationToken);
                foreach (var line in processes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var fields = line.Split(',').Select(x => x.Trim()).ToArray(); if (fields.Length < 3) continue;
                    var gpu = gpus.FirstOrDefault(x => x?["uuid"]?.ToString() == fields[0]);
                    if (gpu is null || !int.TryParse(fields[1], out var pid)) continue;
                    gpu["processes"]!.AsArray().Add(new JsonObject { ["pid"] = pid, ["memoryMiB"] = double.TryParse(fields[2], CultureInfo.InvariantCulture, out var memory) ? memory : null,
                        ["kind"] = "other", ["servedName"] = "", ["serverPid"] = null, ["user"] = "", ["containerId"] = "", ["command"] = "", ["source"] = "nvidia-smi" });
                }
            }
            catch (FleetException) { /* GPU readings remain useful when process reporting is unavailable. */ }
            return new JsonObject { ["ok"] = true, ["error"] = null, ["warnings"] = new JsonArray(), ["source"] = "nvidia-smi", ["driver"] = new JsonObject { ["kernelModule"] = driver, ["library"] = driver }, ["gpus"] = gpus };
        }
        catch (FleetException ex)
        {
            return new JsonObject { ["ok"] = false, ["error"] = new JsonObject { ["code"] = ex.Code, ["title"] = "GPU readings unavailable", ["message"] = ex.Message, ["hint"] = "Check the GPU driver and NVIDIA management tools on this host." },
                ["warnings"] = new JsonArray(), ["source"] = "none", ["driver"] = new JsonObject { ["kernelModule"] = "", ["library"] = "" }, ["gpus"] = new JsonArray() };
        }
    }
}
