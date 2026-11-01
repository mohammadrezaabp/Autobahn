using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Autobahn.Application.Fleet;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autobahn.Infrastructure.Fleet;

public sealed class FleetPoller(IFleetAgentClient agents, IOptions<FleetOptions> options, FleetMetrics metrics, ILogger<FleetPoller> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, JsonNode> _snapshots = new();
    private readonly ConcurrentDictionary<string, Queue<JsonNode>> _history = new();
    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
    public JsonNode Snapshot() => new JsonObject
    {
        ["time"] = Now, ["pollSeconds"] = options.Value.PollSeconds,
        ["nodes"] = new JsonArray(agents.Nodes.Select(node => _snapshots.TryGetValue(node.Id, out var value) ? value.DeepClone() : new JsonObject
        { ["id"] = node.Id, ["address"] = node.Address, ["reachable"] = null, ["status"] = null, ["error"] = null, ["lastOkAt"] = null, ["lastPollAt"] = null }).ToArray())
    };

    public JsonNode History(string id)
    {
        JsonArray points = [];
        if (_history.TryGetValue(id, out var queue)) lock (queue) points = new JsonArray(queue.Select(x => x.DeepClone()).ToArray());
        return new JsonObject { ["id"] = id, ["fields"] = new JsonArray("memoryUsedMiB", "utilizationPct", "temperatureC", "powerW"), ["points"] = points };
    }

    public async Task PollAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(agents.Nodes.Select(async node =>
        {
            var row = new JsonObject { ["id"] = node.Id, ["address"] = node.Address, ["lastPollAt"] = Now };
            try
            {
                var status = await agents.CallAsync(node.Id, HttpMethod.Get, "/v1/status", cancellationToken: cancellationToken);
                row["reachable"] = true; row["status"] = status; row["lastOkAt"] = Now; row["error"] = null;
                var readings = new JsonObject();
                foreach (var gpu in status["gpu"]?["gpus"]?.AsArray() ?? [])
                    if (gpu is not null) readings[gpu["index"]!.ToString()] = new JsonArray(new[] { "memoryUsedMiB", "utilizationPct", "temperatureC", "powerW" }.Select(key => gpu[key]?.DeepClone()).ToArray());
                var queue = _history.GetOrAdd(node.Id, _ => new Queue<JsonNode>());
                lock (queue)
                {
                    queue.Enqueue(new JsonObject { ["t"] = Now, ["gpus"] = readings });
                    while (queue.Count > Math.Clamp(3600 / Math.Max(1, options.Value.PollSeconds), 1, 3600)) queue.Dequeue();
                }
            }
            catch (FleetException ex)
            {
                row["reachable"] = false; row["status"] = null;
                row["lastOkAt"] = _snapshots.TryGetValue(node.Id, out var previous) ? previous["lastOkAt"]?.DeepClone() : null;
                row["error"] = new JsonObject { ["code"] = ex.Code, ["message"] = ex.Message };
                logger.LogWarning("Fleet host {Host} is unavailable: {Code}", node.Id, ex.Code);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not OutOfMemoryException)
            {
                row["reachable"] = false; row["status"] = null; row["lastOkAt"] = null;
                row["error"] = new JsonObject { ["code"] = "invalid_agent_response", ["message"] = "The agent returned an invalid status report." };
                logger.LogWarning("Fleet host {Host} returned an invalid status report", node.Id);
            }
            _snapshots[node.Id] = row;
            metrics.Update(node.Id, row);
        }));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollSeconds, 2, 300)));
        do { await PollAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
