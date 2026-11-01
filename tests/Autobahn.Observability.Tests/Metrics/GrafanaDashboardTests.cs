using System.Text.Json;
using System.Text.RegularExpressions;

namespace Autobahn.Observability.Tests.Metrics;

/// <summary>
/// The provisioned boards must keep querying series the gateway actually exports.
/// A renamed instrument or a typo in a panel otherwise ships as an empty chart.
/// </summary>
public sealed class GrafanaDashboardTests
{
    private static readonly Regex MetricName = new(
        @"\b(?:gateway_[a-z0-9_]+|dotnet_[a-z0-9_]+|ALERTS|up)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly string[] DashboardFiles =
    [
        "Autobahn-gateway.json",
        "Autobahn-gateway-traffic.json",
        "Autobahn-models.json",
        "Autobahn-platform.json",
        "Autobahn-finops.json",
        "Autobahn-tenants.json",
        "Autobahn-capacity.json",
    ];

    private static readonly HashSet<string> AllowedMetrics =
    [
        "gateway_inference_requests_total",
        "gateway_inference_errors_total",
        "gateway_inference_duration_seconds",
        "gateway_time_to_first_token_seconds",
        "gateway_active_streams",
        "gateway_active_requests",
        "gateway_rate_limit_rejections_total",
        "gateway_quota_rejections_total",
        "gateway_tokens_total",
        "gateway_usage_parse_failures_total",
        "gateway_usage_unsplit_total",
        "gateway_usage_estimated_total",
        "gateway_inference_route_total",
        "gateway_forward_attempts_total",
        "gateway_model_resolve_total",
        "gateway_circuit_breaker_transitions_total",
        "gateway_bulkhead_rejections_total",
        "gateway_bulkhead_inflight",
        "gateway_bulkhead_queued",
        "gateway_usage_writer_queue_depth",
        "gateway_usage_writer_dropped_total",
        "gateway_billing_reconciliation_runs_total",
        "gateway_billing_reconciliation_discrepancies",
        "gateway_billing_reconciliation_cost_drift",
        "gateway_billed_cost_dollars_total",
        "gateway_billing_cost_dollars",
        "gateway_unpriced_models",
        "gateway_anonymous_request_share",
        "gateway_budget_spend_ratio",
        "gateway_quota_used_ratio",
        "gateway_tenant_cost_month_to_date_dollars",
        "gateway_api_key_cost_dollars",
        "gateway_api_keys",
        "gateway_backend_health",
        "gateway_models_configured",
        "gateway_circuit_breaker_state",
        "gateway_rate_limit_adaptive_factor",
        "gateway_rate_limit_partitions",
        "gateway_rate_limit_forced_evictions_total",
        "gateway_rate_limit_backed_off_partitions",
        "gateway_rate_limit_decisions_total",
        "gateway_rate_limit_utilization_ratio",
        "gateway_key_open_streams",
        "gateway_key_open_streams_peak",
        "gateway_upstream_duration_seconds",
        "gateway_bulkhead_queue_wait_seconds",
        "gateway_process_start_time_seconds",
        "gateway_build_info",
        "gateway_config_version",
        "dotnet_process_memory_working_set_bytes",
        "dotnet_gc_heap_total_allocated_bytes_total",
        "dotnet_gc_last_collection_heap_size_bytes",
        "dotnet_gc_collections_total",
        "dotnet_gc_pause_time_seconds_total",
        "dotnet_thread_pool_queue_length_total",
        "up",
        "ALERTS",
    ];

    [Fact]
    public void Dashboards_ReferenceKnownMetrics_AndTheNewBoards()
    {
        var root = Path.Combine(FindRepoRoot(), "deploy", "grafana", "dashboards");
        var titles = new Dictionary<string, HashSet<string>>();

        foreach (var file in DashboardFiles)
        {
            var json = File.ReadAllText(Path.Combine(root, file));
            json.Should().NotContain("process_runtime_dotnet_");
            using var doc = JsonDocument.Parse(json);
            var uid = doc.RootElement.GetProperty("uid").GetString();
            uid.Should().NotBeNullOrEmpty();
            titles[uid!] = new HashSet<string>(StringComparer.Ordinal);

            foreach (var panel in doc.RootElement.GetProperty("panels").EnumerateArray())
            {
                if (panel.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                {
                    titles[uid].Add(title.GetString()!);
                }

                if (!panel.TryGetProperty("targets", out var targets))
                {
                    continue;
                }

                foreach (var target in targets.EnumerateArray())
                {
                    if (!target.TryGetProperty("expr", out var exprEl))
                    {
                        continue;
                    }

                    var expr = exprEl.GetString() ?? "";
                    foreach (Match match in MetricName.Matches(expr))
                    {
                        AllowedMetrics.Should().Contain(
                            Normalize(match.Value),
                            $"{file} queries unknown metric '{match.Value}'");
                    }
                }
            }
        }

        titles.Keys.Should().BeEquivalentTo(
            ["Autobahn-gateway", "Autobahn-gateway-traffic", "Autobahn-models", "Autobahn-platform", "Autobahn-finops", "Autobahn-tenants", "Autobahn-capacity"]);
        titles["Autobahn-tenants"].Should().Contain(
            ["Requests / s by tenant", "Requests / s by key", "Error ratio by tenant", "Share of each model by key", "Open streams by key", "Tokens / s by tenant", "Refusal ratio by tenant", "Refusals / s by limit", "Refusal ratio by key", "Request duration p95 by tenant"]);
        titles["Autobahn-capacity"].Should().Contain(
            ["Uptime", "Release", "Configuration version", "Model limit utilization", "Adaptive factor", "In flight and queued", "Queue wait p95", "Upstream duration p95", "Gateway overhead (mean)", "Answers by upstream status"]);
        titles["Autobahn-models"].Should().Contain(
            ["Model scoreboard", "Backend health", "Circuit breaker", "Request duration", "Time to first token", "Bulkhead in-flight and queue"]);
        titles["Autobahn-platform"].Should().Contain(
            ["Scrape up", "Models configured", "Partition fill", "Forced evictions", "Adaptive factor", "Backed-off partitions", "Rate limit rejections / s", "Working set", "Large object heap", "Bytes allocated per request", "Gen2 collections / s", "GC pause / s", "Thread pool queue"]);
        titles["Autobahn-finops"].Should().Contain(
            ["Cost today", "Month to date", "Unpriced models", "Anonymous request share", "Spend by tenant", "Spend by model", "Budget utilization", "Top API keys", "Keys expiring", "Idle keys", "Quota utilization"]);
    }

    [Fact]
    public void OperatorBoard_DoesNotFloorTheErrorRate_OrCopyTheLatencyThresholdOntoRates()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(DashboardDir(), "Autobahn-gateway.json")));
        var panels = doc.RootElement.GetProperty("panels").EnumerateArray().ToList();

        var error = panels.Single(p => p.GetProperty("title").GetString() == "Error rate");
        var expr = error.GetProperty("targets")[0].GetProperty("expr").GetString()!;
        expr.Should().Contain(">= 0.05");
        expr.Should().NotContain("clamp_min");

        foreach (var title in new[] { "Request rate", "In-flight requests", "Active streams" })
        {
            var panel = panels.Single(p => p.GetProperty("title").GetString() == title);
            var steps = panel.GetProperty("fieldConfig").GetProperty("defaults").GetProperty("thresholds").GetProperty("steps");
            foreach (var step in steps.EnumerateArray())
            {
                if (step.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number)
                {
                    value.GetInt32().Should().NotBe(80, title);
                }
            }
        }

        var variable = doc.RootElement.GetProperty("templating").GetProperty("list").EnumerateArray()
            .Single(v => v.GetProperty("name").GetString() == "model");
        variable.GetProperty("allValue").GetString().Should().Be(".*");
        variable.GetProperty("definition").GetString().Should().Contain("gateway_backend_health");
    }

    [Fact]
    public void ModelsScoreboard_RenamesJoinedValueColumns()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(DashboardDir(), "Autobahn-models.json")));
        var panel = doc.RootElement.GetProperty("panels").EnumerateArray()
            .Single(p => p.TryGetProperty("title", out var title) && title.GetString() == "Model scoreboard");
        var organize = panel.GetProperty("transformations").EnumerateArray()
            .Single(t => t.GetProperty("id").GetString() == "organize");
        var rename = organize.GetProperty("options").GetProperty("renameByName");

        rename.GetProperty("Value #A").GetString().Should().Be("Health");
        rename.GetProperty("Value #B").GetString().Should().Be("Breaker");
        rename.GetProperty("Value #C").GetString().Should().Be("In flight");
        rename.GetProperty("Value #D").GetString().Should().Be("Queued");
        rename.GetProperty("Value #E").GetString().Should().Be("Error ratio");
        rename.GetProperty("Value #F").GetString().Should().Be("TTFT p95");
        rename.GetProperty("Value #G").GetString().Should().Be("Duration p99");
        rename.GetProperty("Value #H").GetString().Should().Be("Tokens/s");
    }

    [Fact]
    public void Alerts_CoverBudgetHardStopAndUnpricedModels()
    {
        var yaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "deploy", "prometheus", "alerts", "Autobahn.yml"));
        yaml.Should().Contain("alert: GatewayBudgetHardStop");
        yaml.Should().Contain("gateway_budget_spend_ratio{hard_stop=\"true\"}");
        yaml.Should().Contain("alert: GatewayUnpricedModels");
        yaml.Should().Contain("max(gateway_unpriced_models) > 0");
        yaml.Should().Contain("for: 1h");
    }

    private static string Normalize(string metric)
    {
        if (metric is "up" or "ALERTS")
        {
            return metric;
        }

        foreach (var suffix in new[] { "_bucket", "_sum", "_count" })
        {
            if (metric.EndsWith(suffix, StringComparison.Ordinal) && AllowedMetrics.Contains(metric[..^suffix.Length]))
            {
                return metric[..^suffix.Length];
            }
        }

        return metric;
    }

    private static string DashboardDir() => Path.Combine(FindRepoRoot(), "deploy", "grafana", "dashboards");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Autobahn.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
