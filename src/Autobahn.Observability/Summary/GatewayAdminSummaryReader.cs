using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;
using Autobahn.Domain.Models.Overview;
using Autobahn.Observability.Runtime;

namespace Autobahn.Observability.Summary;

/// <summary>
/// Builds the Overview summary from the runtime state plus whatever optional producers are wired:
/// registry and health for the backends card, circuit/bulkhead sources, the pipeline/policy/
/// control-plane hot sections, the slow-section cache and the attention evaluator. Every optional
/// dependency is null-tolerant so the reader works in a bare Observability host and in tests.
/// </summary>
public sealed class GatewayAdminSummaryReader(
    GatewayRuntimeState runtimeState,
    TimeProvider? timeProvider = null,
    IModelRegistry? registry = null,
    IBackendHealthStore? healthStore = null,
    ICircuitBreakerStateSource? circuitBreakers = null,
    IBulkheadStateSource? bulkheads = null,
    IAttentionEvaluator? attention = null,
    IOverviewSlowSectionCache? slowSections = null,
    IOverviewHotSectionSource? hotSections = null,
    IBackendInFlightSource? inFlight = null) : IAdminSummaryReader
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _memoSync = new();
    private (long Version, long Second, AdminSummarySnapshot Snapshot)? _memo;

    public AdminSummarySnapshot GetSnapshot()
    {
        // Several live-stream subscribers and the poll endpoint all read the same instant; building
        // the windows and series once per (version, second) keeps that O(1) instead of O(subscribers).
        var version = runtimeState.Version;
        var second = _time.GetUtcNow().ToUnixTimeSeconds();
        lock (_memoSync)
        {
            if (_memo is { } memo && memo.Version == version && memo.Second == second)
            {
                return memo.Snapshot;
            }
        }

        var snapshot = Build();
        lock (_memoSync)
        {
            _memo = (version, second, snapshot);
        }

        return snapshot;
    }

    private AdminSummarySnapshot Build()
    {
        var now = _time.GetUtcNow();
        var (total, errors, avgMs, activeStreams, rateLimit, quota) = runtimeState.GetStats();
        var elapsed = now - runtimeState.StartedUtc;
        var totalSeconds = (long)Math.Max(0, elapsed.TotalSeconds);
        var windows = runtimeState.Windows;
        var windowList = windows.Enabled ? windows.GetStandardWindows() : null;
        var backends = BuildBackends(windowList);
        var pipeline = hotSections?.GetPipeline();
        var policy = hotSections?.GetPolicy();
        var controlPlane = hotSections?.GetControlPlane();

        IReadOnlyList<AttentionItem>? attentionItems = null;
        if (attention is not null)
        {
            attentionItems = attention.Evaluate(new AttentionInputs
            {
                Now = now,
                Windows = windowList,
                Backends = backends,
                Pipeline = pipeline,
                FinOps = slowSections?.FinOps,
                Policy = slowSections?.Policy,
                ControlPlane = slowSections?.ControlPlane,
                Tenants = slowSections?.Tenants,
                RateLimits = slowSections?.RateLimits,
                DatabaseConfigured = slowSections?.ControlPlane?.Database.Configured ?? false,
            });
        }

        return new AdminSummarySnapshot
        {
            Uptime = FormatUptime(elapsed),
            UptimeSeconds = totalSeconds,
            TotalInferenceRequests = total,
            TotalErrors = errors,
            ClientDisconnects = runtimeState.GetClientDisconnects(),
            AverageLatencyMs = avgMs,
            ActiveStreams = activeStreams,
            ActiveRequests = runtimeState.GetActiveRequests(),
            ActiveRequestsPerModel = runtimeState.GetActiveRequestsPerModel(),
            RateLimitRejections = rateLimit,
            QuotaRejections = quota,
            RequestsPerModel = runtimeState.GetRequestsPerModel(),
            ErrorsPerModel = runtimeState.GetErrorsPerModel(),
            Windows = windowList,
            Series = windows.Enabled ? windows.GetSeries() : null,
            Backends = backends,
            Attention = attentionItems,
            Pipeline = pipeline,
            Policy = policy,
            ControlPlane = controlPlane,
        };
    }

    private IReadOnlyList<BackendOverview>? BuildBackends(IReadOnlyList<WindowStats>? windowList)
    {
        if (registry is null || healthStore is null)
        {
            return null;
        }

        var circuits = circuitBreakers?.GetStates()
                .ToDictionary(s => ModelBackends.Key(s.ModelId, s.BackendName), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, CircuitBreakerModelState>(StringComparer.OrdinalIgnoreCase);
        var bulk = bulkheads?.GetStates().ToDictionary(s => s.ModelId, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, BulkheadModelState>(StringComparer.OrdinalIgnoreCase);
        var fiveMinute = windowList?.FirstOrDefault(w => w.Window == "5m")?.PerModel
            .ToDictionary(m => m.ModelId, StringComparer.OrdinalIgnoreCase);

        var models = registry.GetAllModels();
        var list = new List<BackendOverview>(models.Count);
        foreach (var m in models)
        {
            bulk.TryGetValue(m.Id, out var bulkhead);
            WindowModelStats? recent = null;
            fiveMinute?.TryGetValue(m.Id, out recent);
            var backends = m.GetBackends();

            // A serving model with nothing to forward to has no backend to show as down, and
            // would otherwise be missing from the one place an operator looks for what is broken.
            if (backends.Count == 0 && m.IsServing())
            {
                list.Add(new BackendOverview
                {
                    ModelId = m.Id,
                    Backend = string.Empty,
                    Url = string.Empty,
                    Alias = m.Aliases.Count > 0 ? m.Aliases[0] : null,
                    IsHealthy = false,
                    Error = "This model has no backends, so it serves nothing. Add one, or stop the model.",
                    Requests5m = recent?.Requests ?? 0,
                    ErrorRate5m = recent?.ErrorRate,
                    LatencyP95Ms5m = recent?.LatencyP95Ms,
                });
                continue;
            }

            foreach (var b in backends)
            {
                var health = healthStore.GetHealth(m.Id, b.Name);
                circuits.TryGetValue(ModelBackends.Key(m.Id, b.Name), out var circuit);

                list.Add(new BackendOverview
                {
                    ModelId = m.Id,
                    Backend = b.Name,
                    Url = b.Url,
                    State = m.IsServing() && b.IsServing() ? ModelRouteStates.Serving : ModelRouteStates.Stopped,
                    MaxContextLength = b.MaxContextLength,
                    BackendInFlight = inFlight?.GetInFlight(m.Id, b.Name) ?? 0,
                    Alias = m.Aliases.Count > 0 ? m.Aliases[0] : null,
                    IsHealthy = healthStore.IsBackendHealthy(m.Id, b.Name),
                    StatusCode = health?.StatusCode,
                    Error = health?.Error,
                    LastCheckedUtc = health?.LastCheckedUtc,
                    LastTransitionUtc = health?.LastTransitionUtc,
                    CircuitState = circuit is null ? "unknown" : CircuitLabel(circuit.State),
                    CircuitOpenedAt = circuit?.OpenedAt,
                    CircuitFailures = circuit?.FailuresInWindow ?? 0,
                    CircuitOutcomes = circuit?.OutcomesInWindow ?? 0,
                    CircuitLastTransitionUtc = circuit?.LastTransitionUtc,
                    InFlight = bulkhead?.InFlight ?? 0,
                    Queued = bulkhead?.Queued ?? 0,
                    MaxConcurrent = bulkhead?.MaxConcurrent ?? 0,
                    MaxQueued = bulkhead?.MaxQueued ?? 0,
                    Requests5m = recent?.Requests ?? 0,
                    ErrorRate5m = recent?.ErrorRate,
                    LatencyP95Ms5m = recent?.LatencyP95Ms,
                });
            }
        }

        // Trouble first: unhealthy, then open circuits, then by name.
        list.Sort(static (a, b) =>
        {
            var byHealth = a.IsHealthy.CompareTo(b.IsHealthy);
            if (byHealth != 0) return byHealth;
            var byCircuit = (b.CircuitState == "open").CompareTo(a.CircuitState == "open");
            if (byCircuit != 0) return byCircuit;
            var byModel = string.Compare(a.ModelId, b.ModelId, StringComparison.OrdinalIgnoreCase);
            return byModel != 0 ? byModel : string.Compare(a.Backend, b.Backend, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    private static string CircuitLabel(int state) => state switch
    {
        0 => "closed",
        1 => "half_open",
        2 => "open",
        _ => "unknown",
    };

    private static string FormatUptime(TimeSpan elapsed)
    {
        var days = (int)elapsed.TotalDays;
        var remainder = elapsed - TimeSpan.FromDays(days);
        return $"{days:D2}.{remainder.Hours:D2}:{remainder.Minutes:D2}:{remainder.Seconds:D2}";
    }
}
