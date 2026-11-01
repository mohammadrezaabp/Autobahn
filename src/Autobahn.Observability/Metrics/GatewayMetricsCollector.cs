using System.Diagnostics;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;
using Autobahn.Domain.Models.Overview;
using Autobahn.Application.Observability;
using Autobahn.Domain.RateLimiting;
using Autobahn.Observability.Policy;
using Autobahn.Observability.Runtime;

namespace Autobahn.Observability.Metrics;

public sealed class GatewayMetricsCollector(
    GatewayRuntimeState runtimeState,
    PolicyPressureTracker? policy = null,
    MetricCallerBudget? callers = null) : IGatewayMetricsCollector, IUsageQualityCounters
{
    /// <summary>The <c>model</c> value for a decision made before the request body was parsed.</summary>
    public const string UnknownModel = "unknown";

    /// <summary>The <c>scope</c> value on an admission: no scope refused.</summary>
    public const string NoScope = "none";

    /// <summary>The <c>upstream_status</c> value for a forward the upstream never answered.</summary>
    public const string NoUpstreamStatus = "none";

    // One string per status, built once, so the label costs no allocation per request.
    private static readonly string[] UpstreamStatusLabels =
        Enumerable.Range(100, 500)
            .Select(static status => status.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

    private readonly MetricCallerBudget _callers = callers ?? new MetricCallerBudget();

    private long _parseFailures;
    private long _estimatedUsage;
    private long _unsplitUsage;
    private long _droppedEvents;

    public long ParseFailures => Interlocked.Read(ref _parseFailures);

    public long EstimatedUsage => Interlocked.Read(ref _estimatedUsage);

    public long UnsplitUsage => Interlocked.Read(ref _unsplitUsage);

    public long DroppedEvents => Interlocked.Read(ref _droppedEvents);

    public void RecordRateLimitRejection(string reason) => RecordRateLimitRejection(reason, tenantId: null, modelId: null);

    public void RecordRateLimitRejection(string reason, string? tenantId, string? modelId)
    {
        var kind = reason.Contains("stream", StringComparison.OrdinalIgnoreCase)
            ? RejectionReason.StreamConcurrency
            : RejectionReason.RateLimit;
        runtimeState.RecordRateLimitRejection(kind, modelId);
        policy?.RecordRejection(kind, tenantId, modelId);
        GatewayMeters.RateLimitRejections.Add(
            1,
            new KeyValuePair<string, object?>("reason", reason));
    }

    public void RecordRateLimitDecision(
        MetricCaller caller,
        string? modelId,
        RateLimitScope? scope,
        RateLimitControl control,
        bool admitted)
    {
        var resolved = _callers.Resolve(caller);
        GatewayMeters.RateLimitDecisions.Add(
            1,
            new TagList
            {
                { "tenant", resolved.Tenant },
                { "key", resolved.Key },
                { "model", string.IsNullOrWhiteSpace(modelId) ? UnknownModel : modelId },
                { "scope", admitted ? NoScope : scope?.ToLabel() ?? RateLimitScopeNames.Tenant },
                { "control", control.ToLabel() },
                { "outcome", admitted ? "admitted" : "refused" },
            });
    }

    public void RecordQuotaRejection() => RecordQuotaRejection(tenantId: null, modelId: null);

    public void RecordQuotaRejection(string? tenantId, string? modelId)
    {
        runtimeState.RecordQuotaRejection(RejectionReason.Quota, modelId);
        policy?.RecordRejection(RejectionReason.Quota, tenantId, modelId);
        GatewayMeters.QuotaRejections.Add(1);
    }

    public void RecordBudgetRejection(string? tenantId, string? budgetName, string modelId)
    {
        // The request itself completes as a failed inference (the scope's outcome), so only the
        // reason breakdown and the quota-blocked counter see it — never a second request.
        runtimeState.RecordQuotaRejection(RejectionReason.Budget, modelId);
        policy?.RecordBudgetRejection(tenantId, budgetName, modelId);
    }

    public void RecordGrantDenial(string? tenantId, string modelId)
    {
        policy?.RecordGrantDenial(tenantId, modelId);
    }

    public void RecordModelResolve(string result, string? requestedModel)
    {
        if (result == "not_found")
        {
            runtimeState.RecordReasonOnly(RejectionReason.ModelNotFound, modelId: null);
            if (requestedModel is not null)
            {
                policy?.RecordUnknownModel(requestedModel);
            }
        }

        RecordModelResolve(result);
    }

    public void RecordTokenUsage(string modelId, long promptTokens, long completionTokens) =>
        RecordTokenUsage(modelId, promptTokens, completionTokens, MetricCaller.Anonymous);

    public void RecordTokenUsage(string modelId, long promptTokens, long completionTokens, MetricCaller caller) =>
        GatewayTokenMetricsRecorder.Record(modelId, promptTokens, completionTokens, _callers.Resolve(caller));

    public void RecordTokenUsage(
        string modelId,
        long promptTokens,
        long completionTokens,
        MetricCaller caller,
        string? provider) =>
        GatewayTokenMetricsRecorder.Record(
            modelId,
            promptTokens,
            completionTokens,
            _callers.Resolve(caller),
            string.IsNullOrWhiteSpace(provider) ? ModelProviders.SelfHosted : provider);

    public void RecordProviderSpend(string provider, double costDollars, int requests)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return;
        }

        var tag = new KeyValuePair<string, object?>("provider", provider);
        if (requests > 0)
        {
            GatewayMeters.ProviderRequests.Add(requests, tag);
        }

        if (costDollars > 0 && !double.IsNaN(costDollars) && !double.IsInfinity(costDollars))
        {
            GatewayMeters.ProviderSpendDollars.Add(costDollars, tag);
        }
    }

    public void RecordProviderAccountRefusal(string provider, int upstreamStatus) =>
        GatewayMeters.ProviderAccountRefusals.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("upstream_status", upstreamStatus));

    public void RecordSpendCapRejection(string provider, string scope, string? tenantSlug, string modelId) =>
        GatewayMeters.SpendCapRejections.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("scope", scope),
            new KeyValuePair<string, object?>("tenant", string.IsNullOrWhiteSpace(tenantSlug)
                ? BillingMetricLabels.UnknownTenant
                : tenantSlug),
            new KeyValuePair<string, object?>("model", modelId));

    public void RecordStandbyServed(string modelId, string provider) =>
        GatewayMeters.StandbyServed.Add(
            1,
            new KeyValuePair<string, object?>("model", modelId),
            new KeyValuePair<string, object?>("provider", provider));

    public void RecordUsageParseFailure(string modelId)
    {
        Interlocked.Increment(ref _parseFailures);
        GatewayMeters.UsageParseFailures.Add(1, new KeyValuePair<string, object?>("model", modelId));
    }

    public void RecordEstimatedUsage(string modelId)
    {
        Interlocked.Increment(ref _estimatedUsage);
        GatewayMeters.EstimatedUsage.Add(1, new KeyValuePair<string, object?>("model", modelId));
    }

    public void RecordUnsplitUsage(string modelId)
    {
        Interlocked.Increment(ref _unsplitUsage);
        GatewayMeters.UnsplitUsage.Add(1, new KeyValuePair<string, object?>("model", modelId));
    }

    public void RecordAudioTokens(string modelId, long tokens) =>
        GatewayMeters.AudioTokens.Add(tokens, new KeyValuePair<string, object?>("model", modelId));

    public void RecordInferenceRouted(string modelId, string route, bool isStreaming) =>
        GatewayMeters.InferenceRoute.Add(
            1,
            new KeyValuePair<string, object?>("model", modelId),
            new KeyValuePair<string, object?>("route", route),
            new KeyValuePair<string, object?>("stream", isStreaming ? "true" : "false"));

    public void RecordForwardAttempt(string modelId, string outcome) =>
        RecordForwardAttempt(modelId, outcome, NoUpstreamStatus);

    public void RecordForwardAttempt(string modelId, string outcome, int upstreamStatus) =>
        RecordForwardAttempt(modelId, outcome, UpstreamStatusLabel(upstreamStatus));

    private static void RecordForwardAttempt(string modelId, string outcome, string upstreamStatus) =>
        GatewayMeters.ForwardAttempts.Add(
            1,
            new KeyValuePair<string, object?>("model", modelId),
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("upstream_status", upstreamStatus));

    /// <summary>The label for a status the upstream answered with; anything outside 100 to 599 is <c>other</c>.</summary>
    internal static string UpstreamStatusLabel(int status) =>
        status is >= 100 and <= 599 ? UpstreamStatusLabels[status - 100] : "other";

    public void RecordUpstreamDuration(string modelId, double seconds) =>
        GatewayMeters.UpstreamDuration.Record(seconds, new KeyValuePair<string, object?>("model", modelId));

    public void RecordBulkheadQueueWait(string modelId, double seconds) =>
        GatewayMeters.BulkheadQueueWait.Record(seconds, new KeyValuePair<string, object?>("model", modelId));

    public void RecordModelResolve(string result) =>
        GatewayMeters.ModelResolve.Add(1, new KeyValuePair<string, object?>("result", result));

    public void RecordCircuitBreakerTransition(string modelId, string toState) =>
        GatewayMeters.CircuitBreakerTransitions.Add(
            1,
            new KeyValuePair<string, object?>("model", modelId),
            new KeyValuePair<string, object?>("to_state", toState));

    public void RecordBulkheadRejection(string modelId) =>
        GatewayMeters.BulkheadRejections.Add(1, new KeyValuePair<string, object?>("model", modelId));

    public void RecordBulkheadInflightChange(string modelId, int delta) =>
        GatewayMeters.BulkheadInflight.Add(
            delta,
            new KeyValuePair<string, object?>("model", modelId));

    public void RecordBulkheadQueuedChange(string modelId, int delta) =>
        GatewayMeters.BulkheadQueued.Add(
            delta,
            new KeyValuePair<string, object?>("model", modelId));

    public void RecordTimeToFirstToken(string modelId, double seconds)
    {
        runtimeState.RecordTimeToFirstToken(modelId, seconds * 1_000d);
        GatewayMeters.TimeToFirstToken.Record(
            seconds,
            new KeyValuePair<string, object?>("model", modelId));
    }

    /// <summary>
    /// Shares <c>gateway_usage_writer_dropped_total</c> with the channel-full path: either way the
    /// request never reaches the ledger, and the existing writer-backlog alert should fire.
    /// </summary>
    public void RecordUsageEventsDropped(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _droppedEvents, count);
            GatewayMeters.UsageWriterDropped.Add(count);
        }
    }

    public void RecordBillingReconciliation(int discrepancyCount, double absoluteCostDrift)
    {
        // Recorded as gauges, not counters: the question is "is billing consistent right now", and a
        // monotonic counter cannot answer that — it never returns to zero once a transient
        // discrepancy is resolved. Every completed sweep overwrites both, so a stale value means the
        // sweep itself stopped, which is why the job also emits a heartbeat.
        GatewayMeters.SetBillingReconciliation(discrepancyCount, absoluteCostDrift);
        GatewayMeters.BillingReconciliationRuns.Add(1);
    }

    public void RecordBilledCost(string tenantSlug, string modelId, string costCenter, double costDollars) =>
        RecordBilledCost(tenantSlug, modelId, costCenter, costDollars, ModelProviders.SelfHosted);

    public void RecordBilledCost(string tenantSlug, string modelId, string costCenter, double costDollars, string provider)
    {
        if (costDollars <= 0 || double.IsNaN(costDollars) || double.IsInfinity(costDollars))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            return;
        }

        GatewayMeters.BilledCostDollars.Add(
            costDollars,
            new KeyValuePair<string, object?>("tenant", string.IsNullOrWhiteSpace(tenantSlug)
                ? BillingMetricLabels.UnknownTenant
                : tenantSlug),
            new KeyValuePair<string, object?>("model", modelId),
            new KeyValuePair<string, object?>("cost_center", string.IsNullOrWhiteSpace(costCenter)
                ? BillingMetricLabels.NoCostCenter
                : costCenter),
            new KeyValuePair<string, object?>("provider", string.IsNullOrWhiteSpace(provider)
                ? ModelProviders.SelfHosted
                : provider));
    }
}
