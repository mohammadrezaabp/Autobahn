using System.Diagnostics.Metrics;

namespace Autobahn.Observability.Metrics;

public static class GatewayMeters
{
    public const string MeterName = "Autobahn.Gateway";

    /// <summary>
    /// The series that carry <c>tenant</c> and <c>key</c>. Listed so the exporter can raise their
    /// cardinality limit together: the SDK default of 2,000 points per metric is fewer than fifty
    /// keys across fourteen models and three statuses, and past it the SDK folds new series into
    /// one overflow point without a word.
    /// </summary>
    public static readonly IReadOnlyList<string> CallerLabelledInstruments =
    [
        "gateway_inference_requests_total",
        "gateway_inference_errors_total",
        "gateway_inference_duration_seconds",
        "gateway_tokens_total",
        "gateway_rate_limit_decisions_total",
    ];

    /// <summary>Points each of <see cref="CallerLabelledInstruments"/> may hold.</summary>
    public const int CallerLabelledCardinalityLimit = 10_000;

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> InferenceRequests =
        Meter.CreateCounter<long>("gateway_inference_requests_total");

    public static readonly Counter<long> InferenceErrors =
        Meter.CreateCounter<long>("gateway_inference_errors_total");

    public static readonly Histogram<double> InferenceDuration =
        Meter.CreateHistogram<double>("gateway_inference_duration_seconds");

    public static readonly Histogram<double> TimeToFirstToken =
        Meter.CreateHistogram<double>("gateway_time_to_first_token_seconds");

    /// <summary>
    /// Time the upstream held a forwarded request. Subtract it from
    /// <see cref="InferenceDuration"/> for what the gateway added.
    /// </summary>
    public static readonly Histogram<double> UpstreamDuration =
        Meter.CreateHistogram<double>("gateway_upstream_duration_seconds");

    /// <summary>Wait in the per-model bulkhead queue, for the requests that had to queue.</summary>
    public static readonly Histogram<double> BulkheadQueueWait =
        Meter.CreateHistogram<double>("gateway_bulkhead_queue_wait_seconds");

    public static readonly UpDownCounter<long> ActiveStreams =
        Meter.CreateUpDownCounter<long>("gateway_active_streams");

    /// <summary>Inference requests being forwarded right now — streaming and non-streaming alike.</summary>
    public static readonly UpDownCounter<long> ActiveRequests =
        Meter.CreateUpDownCounter<long>("gateway_active_requests");

    public static readonly Counter<long> RateLimitRejections =
        Meter.CreateCounter<long>("gateway_rate_limit_rejections_total");

    /// <summary>
    /// Every decision of the inference rate limiter, by caller. Labels are tenant slug, key label,
    /// model, scope, control and outcome. <see cref="RateLimitRejections"/> stays as it is: it also
    /// counts the control-plane and failed-credential limiters, which have no caller to name.
    /// </summary>
    public static readonly Counter<long> RateLimitDecisions =
        Meter.CreateCounter<long>("gateway_rate_limit_decisions_total");

    public static readonly Counter<long> QuotaRejections =
        Meter.CreateCounter<long>("gateway_quota_rejections_total");

    public static readonly Counter<long> TokensTotal =
        Meter.CreateCounter<long>("gateway_tokens_total");

    public static readonly Counter<long> UsageParseFailures =
        Meter.CreateCounter<long>("gateway_usage_parse_failures_total");

    /// <summary>
    /// Responses whose upstream reported only a combined token total. Their cost is an approximation
    /// (priced at the dearer rate), so a persistently non-zero value for a model is a signal to check
    /// that upstream's usage reporting.
    /// </summary>
    public static readonly Counter<long> UnsplitUsage =
        Meter.CreateCounter<long>("gateway_usage_unsplit_total");

    /// <summary>
    /// Audio tokens reported in responses, by model. Priced as text by a model's price list, so a
    /// rising count on a model without a reported cost is spend the ledger under-states.
    /// </summary>
    public static readonly Counter<long> AudioTokens =
        Meter.CreateCounter<long>("gateway_usage_audio_tokens_total");

    /// <summary>
    /// Responses billed from a streamed-frame estimate rather than authoritative usage. A rising
    /// value for one tenant may indicate deliberate disconnect-before-completion.
    /// </summary>
    public static readonly Counter<long> EstimatedUsage =
        Meter.CreateCounter<long>("gateway_usage_estimated_total");

    public static readonly Counter<long> InferenceRoute =
        Meter.CreateCounter<long>("gateway_inference_route_total");

    public static readonly Counter<long> ForwardAttempts =
        Meter.CreateCounter<long>("gateway_forward_attempts_total");

    public static readonly Counter<long> ModelResolve =
        Meter.CreateCounter<long>("gateway_model_resolve_total");

    public static readonly Counter<long> CircuitBreakerTransitions =
        Meter.CreateCounter<long>("gateway_circuit_breaker_transitions_total");

    public static readonly Counter<long> BulkheadRejections =
        Meter.CreateCounter<long>("gateway_bulkhead_rejections_total");

    public static readonly UpDownCounter<long> BulkheadInflight =
        Meter.CreateUpDownCounter<long>("gateway_bulkhead_inflight");

    public static readonly UpDownCounter<long> BulkheadQueued =
        Meter.CreateUpDownCounter<long>("gateway_bulkhead_queued");

    public static readonly UpDownCounter<long> UsageWriterQueueDepth =
        Meter.CreateUpDownCounter<long>("gateway_usage_writer_queue_depth");

    public static readonly Counter<long> UsageWriterDropped =
        Meter.CreateCounter<long>("gateway_usage_writer_dropped_total");

    /// <summary>Completed reconciliation sweeps. A flat line means the job has stopped running.</summary>
    public static readonly Counter<long> BillingReconciliationRuns =
        Meter.CreateCounter<long>("gateway_billing_reconciliation_runs_total");

    /// <summary>
    /// Priced cost of billing events newly written to the ledger, in the default currency.
    /// Labels are tenant slug, model, and cost centre. Sum across replicas; do not put an API key on it.
    /// </summary>
    public static readonly Counter<double> BilledCostDollars =
        Meter.CreateCounter<double>("gateway_billed_cost_dollars_total");

    /// <summary>
    /// Cost of requests written to the ledger for a paid provider, by provider. The spend-spike
    /// alert compares a day of it with the days before.
    /// </summary>
    public static readonly Counter<double> ProviderSpendDollars =
        Meter.CreateCounter<double>("gateway_provider_spend_dollars_total");

    /// <summary>Requests written to the ledger for a paid provider.</summary>
    public static readonly Counter<long> ProviderRequests =
        Meter.CreateCounter<long>("gateway_provider_requests_total");

    /// <summary>
    /// Answers from a paid provider that mean the account is refused: 402 (no credit), 401 and
    /// 403 (key not accepted). The circuit breaker does not count these, so this is where an
    /// empty account shows.
    /// </summary>
    public static readonly Counter<long> ProviderAccountRefusals =
        Meter.CreateCounter<long>("gateway_provider_account_refusals_total");

    /// <summary>Requests a monthly provider cap refused, by the scope whose cap it was.</summary>
    public static readonly Counter<long> SpendCapRejections =
        Meter.CreateCounter<long>("gateway_provider_spend_cap_rejections_total");

    /// <summary>Requests a standby backend carried because no primary could.</summary>
    public static readonly Counter<long> StandbyServed =
        Meter.CreateCounter<long>("gateway_standby_served_total");

    private static int _reconciliationDiscrepancies;
    private static double _reconciliationCostDrift;

    /// <summary>
    /// Rollup buckets whose totals disagree with the billing events behind them, as of the last
    /// sweep. <b>Alert on any non-zero value.</b>
    /// </summary>
    public static readonly ObservableGauge<int> BillingReconciliationDiscrepancies =
        Meter.CreateObservableGauge(
            "gateway_billing_reconciliation_discrepancies",
            static () => Volatile.Read(ref _reconciliationDiscrepancies));

    /// <summary>
    /// Total absolute money difference across those buckets, in the configured default currency.
    /// Reported alongside the count because one bucket out by a large amount and many out by
    /// rounding are very different incidents.
    /// </summary>
    public static readonly ObservableGauge<double> BillingReconciliationCostDrift =
        Meter.CreateObservableGauge(
            "gateway_billing_reconciliation_cost_drift",
            static () => Volatile.Read(ref _reconciliationCostDrift));

    internal static void SetBillingReconciliation(int discrepancyCount, double absoluteCostDrift)
    {
        Volatile.Write(ref _reconciliationDiscrepancies, discrepancyCount);
        Volatile.Write(ref _reconciliationCostDrift, absoluteCostDrift);
    }
}
