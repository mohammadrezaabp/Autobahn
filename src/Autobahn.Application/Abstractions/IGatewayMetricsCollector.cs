using Autobahn.Application.Observability;
using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;

public interface IGatewayMetricsCollector
{
    void RecordRateLimitRejection(string reason);

    /// <summary>
    /// Rate-limit rejection with the dimensions the Overview's policy card needs. The default body
    /// preserves the old single-argument behaviour for implementations that do not track them.
    /// </summary>
    /// <param name="tenantId">The rejected partition (tenant id, or <c>anon:&lt;ip&gt;</c>); null when unknown.</param>
    /// <param name="modelId">Known only for controls that run after routing (stream concurrency); null otherwise.</param>
    void RecordRateLimitRejection(string reason, string? tenantId, string? modelId) => RecordRateLimitRejection(reason);

    /// <summary>
    /// One decision of the inference rate limiter, attributed to the caller it was made for.
    /// </summary>
    /// <remarks>
    /// Counted beside <see cref="RecordRateLimitRejection(string, string?, string?)"/>, not instead
    /// of it: that series says which limit kind is refusing, this one says whom. A request that
    /// passes the rate stage is one admitted decision; a stream cap refusing it afterwards adds a
    /// refused one, so the two outcomes sum to decisions, not to requests.
    /// </remarks>
    /// <param name="modelId">
    /// Null when the decision was made before the body was parsed — which is every identity-scope
    /// decision on a gateway with no model-scoped rule.
    /// </param>
    /// <param name="scope">The scope that refused. Ignored on an admission.</param>
    void RecordRateLimitDecision(
        MetricCaller caller,
        string? modelId,
        RateLimitScope? scope,
        RateLimitControl control,
        bool admitted)
    {
    }

    void RecordQuotaRejection();

    /// <inheritdoc cref="RecordRateLimitRejection(string, string?, string?)"/>
    void RecordQuotaRejection(string? tenantId, string? modelId) => RecordQuotaRejection();

    /// <summary>A hard-stop budget refused the request before forwarding.</summary>
    void RecordBudgetRejection(string? tenantId, string? budgetName, string modelId)
    {
    }

    /// <summary>The API key is not granted the model it asked for.</summary>
    void RecordGrantDenial(string? tenantId, string modelId)
    {
    }

    /// <summary>A resolve outcome plus the name the client actually sent, so unknown models can be listed.</summary>
    void RecordModelResolve(string result, string? requestedModel) => RecordModelResolve(result);

    void RecordTokenUsage(string modelId, long promptTokens, long completionTokens);

    /// <summary>As above, naming the caller the tokens were spent by.</summary>
    void RecordTokenUsage(string modelId, long promptTokens, long completionTokens, MetricCaller caller) =>
        RecordTokenUsage(modelId, promptTokens, completionTokens);

    /// <summary>As above, also naming the provider that served the request.</summary>
    void RecordTokenUsage(
        string modelId,
        long promptTokens,
        long completionTokens,
        MetricCaller caller,
        string? provider) =>
        RecordTokenUsage(modelId, promptTokens, completionTokens, caller);

    void RecordUsageParseFailure(string modelId);

    /// <summary>
    /// The upstream reported only a combined token total for this model, so its cost was priced by
    /// the conservative total-only policy rather than from a real input/output split. Persistently
    /// non-zero for a model means its billing is an approximation.
    /// </summary>
    void RecordUnsplitUsage(string modelId);

    /// <summary>
    /// Audio tokens a response reported. A model's price has no audio rate, so where the provider
    /// does not report the charge they are priced as text — under-billed, since audio is dearer.
    /// </summary>
    void RecordAudioTokens(string modelId, long tokens)
    {
    }

    /// <summary>
    /// A response was billed from an estimate because the authoritative usage never arrived — almost
    /// always a client disconnecting mid-stream. Use it to reconcile approximated spend.
    /// </summary>
    void RecordEstimatedUsage(string modelId);

    void RecordInferenceRouted(string modelId, string route, bool isStreaming);

    void RecordForwardAttempt(string modelId, string outcome);

    /// <summary>As above, for a forward the upstream answered: carries the status it answered with.</summary>
    void RecordForwardAttempt(string modelId, string outcome, int upstreamStatus) =>
        RecordForwardAttempt(modelId, outcome);

    /// <summary>
    /// Time the upstream held one request, from the moment forwarding began until the response was
    /// finished or given up on. Total request time minus this is what the gateway added.
    /// </summary>
    void RecordUpstreamDuration(string modelId, double seconds)
    {
    }

    /// <summary>How long a request waited in the bulkhead queue, whether or not it got a slot.</summary>
    void RecordBulkheadQueueWait(string modelId, double seconds)
    {
    }

    void RecordModelResolve(string result);

    void RecordCircuitBreakerTransition(string modelId, string toState);

    void RecordBulkheadRejection(string modelId);

    void RecordBulkheadInflightChange(string modelId, int delta);

    /// <summary>
    /// Requests currently waiting in the per-model bulkhead queue for a forwarding slot. A
    /// persistently non-zero value means the model is saturated: the GPU (or its
    /// <c>Gateway:Resilience:MaxConcurrentForwardsPerModel</c> ceiling) is the bottleneck.
    /// </summary>
    void RecordBulkheadQueuedChange(string modelId, int delta)
    {
    }

    void RecordTimeToFirstToken(string modelId, double seconds);

    /// <summary>
    /// Publishes the outcome of a billing reconciliation sweep: how many rollup buckets disagreed
    /// with the ledger behind them, and by how much money in total.
    /// </summary>
    /// <remarks>
    /// Alert on a non-zero <paramref name="discrepancyCount"/>. Everything an operator reads comes
    /// from the rollups while the ledger is what records the request, so a divergence between them
    /// produces wrong numbers that look entirely normal — this counter is the only place it surfaces.
    /// A sweep that runs and finds nothing still reports zero, which is what distinguishes "balanced"
    /// from "the job stopped running".
    /// </remarks>
    void RecordBillingReconciliation(int discrepancyCount, double absoluteCostDrift);

    /// <summary>
    /// Priced cost of one billing event that was newly persisted. <paramref name="tenantSlug"/> is
    /// the tenant slug (<c>anonymous</c> for keyless traffic), never a tenant id. The key itself is
    /// not a label: per-key spend is exported separately, capped, from the ledger.
    /// </summary>
    void RecordBilledCost(string tenantSlug, string modelId, string costCenter, double costDollars)
    {
    }

    /// <summary>As above, also naming the provider the cost was paid to.</summary>
    void RecordBilledCost(string tenantSlug, string modelId, string costCenter, double costDollars, string provider) =>
        RecordBilledCost(tenantSlug, modelId, costCenter, costDollars);

    /// <summary>
    /// Cost of requests newly written to the ledger for one paid provider, and how many there
    /// were. The series the spend-spike alert reads.
    /// </summary>
    void RecordProviderSpend(string provider, double costDollars, int requests)
    {
    }

    /// <summary>
    /// A paid provider answered a request with a status that says the account, not the request, is
    /// the problem: 402 for no credit, 401 or 403 for a key it no longer accepts.
    /// </summary>
    void RecordProviderAccountRefusal(string provider, int upstreamStatus)
    {
    }

    /// <summary>A monthly provider cap refused a request before it was forwarded.</summary>
    void RecordSpendCapRejection(string provider, string scope, string? tenantSlug, string modelId)
    {
    }

    /// <summary>A request was carried by a standby backend because no primary could take it.</summary>
    void RecordStandbyServed(string modelId, string provider)
    {
    }

    /// <summary>
    /// Usage events the batch writer gave up on (retries exhausted, or the buffer cap was hit during
    /// an outage). Each one is a request that is NOT in the billing ledger; the Observability
    /// implementation counts it on <c>gateway_usage_writer_dropped_total</c>.
    /// </summary>
    void RecordUsageEventsDropped(int count)
    {
    }
}
