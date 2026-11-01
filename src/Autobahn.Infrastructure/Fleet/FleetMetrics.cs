using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;

namespace Autobahn.Infrastructure.Fleet;

public sealed class FleetMetrics : IDisposable
{
    public const string MeterName = "Autobahn.Fleet";
    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly ConcurrentDictionary<string, JsonNode> _hosts = new();

    public FleetMetrics()
    {
        _meter.CreateObservableGauge("autobahn_fleet_host_reachable", () => _hosts.Select(x => new Measurement<int>(x.Value["reachable"]?.GetValue<bool>() == true ? 1 : 0, new KeyValuePair<string, object?>("host", x.Key))));
        _meter.CreateObservableGauge("autobahn_fleet_disk_free_bytes", () => HostValues("freeBytes"));
        _meter.CreateObservableGauge("autobahn_fleet_gpu_utilization_percent", () => GpuValues("utilizationPct"));
        _meter.CreateObservableGauge("autobahn_fleet_gpu_memory_used_mib", () => GpuValues("memoryUsedMiB"));
        _meter.CreateObservableGauge("autobahn_fleet_gpu_temperature_celsius", () => GpuValues("temperatureC"));
        _meter.CreateObservableGauge("autobahn_fleet_gpu_power_watts", () => GpuValues("powerW"));
    }

    public void Update(string host, JsonNode snapshot) => _hosts[host] = snapshot.DeepClone();

    private IEnumerable<Measurement<double>> HostValues(string field)
    {
        foreach (var (host, row) in _hosts)
            if (row["status"]?["disk"]?[field] is { } number && double.TryParse(number.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var value))
                yield return new Measurement<double>(value, new KeyValuePair<string, object?>("host", host));
    }

    private IEnumerable<Measurement<double>> GpuValues(string field)
    {
        foreach (var (host, row) in _hosts)
            foreach (var gpu in row["status"]?["gpu"]?["gpus"]?.AsArray() ?? [])
                if (gpu?[field] is { } number && double.TryParse(number.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var value))
                    yield return new Measurement<double>(value, new KeyValuePair<string, object?>("host", host), new KeyValuePair<string, object?>("gpu", gpu["index"]!.ToString()));
    }

    public void Dispose() => _meter.Dispose();
}
