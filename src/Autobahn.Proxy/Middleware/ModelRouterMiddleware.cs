using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Application.Diagnostics;
using Autobahn.Application.Forwarding;
using Autobahn.Application.Errors;
using Autobahn.Domain.Models;
using Autobahn.Domain.Identity;
using Autobahn.Application.Observability;
using Autobahn.Domain.RateLimiting;
using Autobahn.Application.Security;
using Autobahn.Application.Usage;
using System.Security.Claims;
using Autobahn.Proxy.Diagnostics;
using Autobahn.Proxy.Errors;
using Autobahn.Proxy.Forwarding;
using Autobahn.Proxy.Parsing;
using Autobahn.Proxy.Resilience;
using Autobahn.Proxy.Routing;
using Yarp.ReverseProxy.Forwarder;

namespace Autobahn.Proxy.Middleware;

public sealed class ModelRouterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IModelRegistry _registry;
    private readonly IBackendHealthStore _healthStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGatewayAuthenticationState _authState;
    private readonly IErrorResponseWriter _errors;
    private readonly IRequestTracker _requestTracker;
    private readonly IRecentRequestStore _recentRequestStore;
    private readonly IUsageRecorder _usageRecorder;
    private readonly IGatewayMetricsCollector _metricsCollector;
    private readonly ModelCircuitBreakerRegistry _circuitBreakers;
    private readonly BulkheadRegistry _bulkhead;
    private readonly BackendInFlightTracker _backendInFlight;
    private readonly IRateLimitPolicyResolver _rateLimitPolicyResolver;
    private readonly IDistributedRateLimitStore _rateLimitStore;
    private readonly IInferenceHttpForwarder _forwarder;
    private readonly InferenceForwardTimeouts _forwardTimeouts;
    private readonly IUpstreamBearerTokenResolver _upstreamBearerTokenResolver;
    private readonly IBudgetEnforcementService _budgetEnforcement;
    private readonly IProviderSpendCapService _spendCaps;
    private readonly IGatewayErrorRecorder _errorRecorder;
    private readonly ILogger<ModelRouterMiddleware> _logger;

    /// <summary>
    /// Optional so the many tests that build this middleware by hand keep compiling. When absent the
    /// stream-concurrency cap falls back to the tenant tier alone, which is what it enforced before
    /// scoped rules existed.
    /// </summary>
    private readonly IRateLimitPlanResolver? _rateLimitPlanResolver;

    private readonly IRateLimitUsageTracker? _rateLimitUsage;

    /// <summary>
    /// The final say on whether a request was admitted. The rate limiter earlier in the pipeline
    /// reports its own rejections, but an admission there is provisional: a streaming request can
    /// still be refused by the concurrency cap below. Recording the admission there and the refusal
    /// here would clear the partition's backoff a few frames before adding to it, so a client stuck
    /// on a concurrency cap could never escalate past one consecutive rejection.
    /// </summary>
    private readonly IAdaptiveRateLimitGovernor? _rateLimitGovernor;

    public ModelRouterMiddleware(
        RequestDelegate next,
        IModelRegistry registry,
        IBackendHealthStore healthStore,
        IServiceScopeFactory scopeFactory,
        IGatewayAuthenticationState authState,
        IErrorResponseWriter errors,
        IRequestTracker requestTracker,
        IRecentRequestStore recentRequestStore,
        IUsageRecorder usageRecorder,
        IGatewayMetricsCollector metricsCollector,
        ModelCircuitBreakerRegistry circuitBreakers,
        BulkheadRegistry bulkhead,
        IRateLimitPolicyResolver rateLimitPolicyResolver,
        IDistributedRateLimitStore rateLimitStore,
        IInferenceHttpForwarder forwarder,
        IOptions<GatewayOptions> options,
        IUpstreamBearerTokenResolver upstreamBearerTokenResolver,
        IBudgetEnforcementService budgetEnforcement,
        IGatewayErrorRecorder errorRecorder,
        ILogger<ModelRouterMiddleware> logger,
        IRateLimitPlanResolver? rateLimitPlanResolver = null,
        IRateLimitUsageTracker? rateLimitUsage = null,
        IAdaptiveRateLimitGovernor? rateLimitGovernor = null,
        BackendInFlightTracker? backendInFlight = null,
        IProviderSpendCapService? spendCaps = null)
    {
        // Optional for the same reason: without one, no paid provider is capped.
        _spendCaps = spendCaps ?? new NoOpProviderSpendCapService();
        // Optional for the tests that build this middleware by hand; a private tracker balances
        // just as well, it only is not the one the admin API reads.
        _backendInFlight = backendInFlight ?? new BackendInFlightTracker();
        _next = next;
        _registry = registry;
        _healthStore = healthStore;
        _scopeFactory = scopeFactory;
        _authState = authState;
        _errors = errors;
        _requestTracker = requestTracker;
        _recentRequestStore = recentRequestStore;
        _usageRecorder = usageRecorder;
        _metricsCollector = metricsCollector;
        _circuitBreakers = circuitBreakers;
        _bulkhead = bulkhead;
        _rateLimitPolicyResolver = rateLimitPolicyResolver;
        _rateLimitStore = rateLimitStore;
        _forwarder = forwarder;
        _forwardTimeouts = InferenceForwardTimeouts.FromResilience(options.Value.Resilience);
        _upstreamBearerTokenResolver = upstreamBearerTokenResolver;
        _budgetEnforcement = budgetEnforcement;
        _errorRecorder = errorRecorder;
        _logger = logger;
        _rateLimitPlanResolver = rateLimitPlanResolver;
        _rateLimitUsage = rateLimitUsage;
        _rateLimitGovernor = rateLimitGovernor;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (InferenceRouteClassifier.IsPassthroughPath(context.Request.Path) ||
            !InferenceRouteClassifier.IsRoutableInference(context))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Request.EnableBuffering();

        // PublicModelDetectionMiddleware has already parsed this body. Reusing its result is what
        // keeps the most expensive step on the inference path to one pass; the fallback covers
        // pipelines (and tests) where that middleware did not run.
        InferenceRequestInfo requestInfo;
        if (InferenceRequestParseCache.TryGet(context, out var cachedInfo))
        {
            if (cachedInfo is null)
            {
                await context.WriteGatewayErrorAsync(
                    _errors.Write(GatewayErrorCode.InvalidJson),
                    context.RequestAborted).ConfigureAwait(false);
                return;
            }

            requestInfo = cachedInfo.Value;
        }
        else
        {
            try
            {
                context.Request.Body.Position = 0;
                requestInfo = await InferenceRequestParser.ParseAsync(context.Request.Body, context.RequestAborted)
                    .ConfigureAwait(false);
                InferenceRequestParseCache.SetParsed(context, requestInfo);
            }
            catch (JsonException)
            {
                await context.WriteGatewayErrorAsync(
                    _errors.Write(GatewayErrorCode.InvalidJson),
                    context.RequestAborted).ConfigureAwait(false);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(requestInfo.Model))
        {
            await context.WriteGatewayErrorAsync(
                _errors.Write(GatewayErrorCode.MissingModel),
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (!_registry.TryGetModel(requestInfo.Model, out var modelConfig) || modelConfig is null)
        {
            _metricsCollector.RecordModelResolve("not_found", requestInfo.Model);
            await context.WriteGatewayErrorAsync(
                _errors.Write(
                    GatewayErrorCode.ModelNotFound,
                    message: $"Model '{requestInfo.Model}' not found."),
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        InferenceAccessLog.SetModel(context, modelConfig.Id);

        // Checked before grants, health and every other admission control: an operator has taken
        // this route out of service, so nothing downstream is entitled to admit it. Answered as
        // model_not_found, which is what GET /v1/models already says about it — a stopped model is
        // absent from the listing, so an OpenAI-compatible client that fails over on 404 does the
        // right thing. The message names the real cause so an operator is not left guessing.
        if (modelConfig.IsStopped())
        {
            _metricsCollector.RecordModelResolve("stopped", requestInfo.Model);
            await RejectAtAdmissionAsync(
                context,
                modelConfig.Id,
                requestInfo.Stream,
                _errors.Write(
                    GatewayErrorCode.ModelNotFound,
                    message: $"Model '{requestInfo.Model}' has been stopped by an administrator."),
                outcome: "model_stopped").ConfigureAwait(false);
            return;
        }

        _metricsCollector.RecordModelResolve(
            string.Equals(requestInfo.Model, modelConfig.Id, StringComparison.OrdinalIgnoreCase)
                ? "resolved"
                : "alias");
        _metricsCollector.RecordInferenceRouted(modelConfig.Id, ClassifyRoute(context.Request.Path), requestInfo.Stream);

        // The caller, when there is one. Read here as well as in the access check below: a public
        // model skips that check, and a standby backend and a spend cap still need to know the key.
        Guid? callerTenantId = Guid.TryParse(context.User.FindFirstValue(GatewayAuthClaims.TenantId), out var claimTenant)
            ? claimTenant
            : null;
        Guid? callerApiKeyId = Guid.TryParse(context.User.FindFirstValue(GatewayAuthClaims.ApiKeyId), out var claimKey)
            ? claimKey
            : null;

        if (_authState.IsAuthenticationRequired && !modelConfig.AllowsPublicGatewayAccess())
        {
            if (context.User.Identity?.IsAuthenticated != true ||
                !Guid.TryParse(context.User.FindFirstValue(GatewayAuthClaims.TenantId), out var tenantId) ||
                !Guid.TryParse(context.User.FindFirstValue(GatewayAuthClaims.ApiKeyId), out var apiKeyId))
            {
                await context.WriteGatewayErrorAsync(
                    _errors.Write(GatewayErrorCode.InvalidApiKey),
                    context.RequestAborted).ConfigureAwait(false);
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var modelGrants = scope.ServiceProvider.GetRequiredService<IModelGrantService>();
            var access = await modelGrants
                .EvaluateAsync(tenantId, apiKeyId, modelConfig.Id, context.RequestAborted)
                .ConfigureAwait(false);
            if (!access.IsAllowed)
            {
                // Counted like the other admission refusals so a key hammering a model it was never
                // granted shows up on the Overview instead of vanishing between the counters.
                _metricsCollector.RecordGrantDenial(tenantId.ToString(), modelConfig.Id);

                // The reason is the error code and the outcome both: a caller can tell which rule
                // to ask about, and the Errors tab can say whose it is to fix.
                var reason = access.Refusal.ToReason()!;
                await RejectAtAdmissionAsync(
                    context,
                    modelConfig.Id,
                    requestInfo.Stream,
                    _errors.Write(
                        ToErrorCode(access.Refusal),
                        message: access.Describe(modelConfig.Id),
                        details: new Dictionary<string, object>
                        {
                            ["reason"] = reason,
                            ["provider"] = access.Provider,
                        }),
                    outcome: reason).ConfigureAwait(false);
                return;
            }
        }

        // One backend of the model takes the request. Choosing it is also the health and circuit
        // admission: a backend is only chosen if it is in service, healthy and its breaker lets
        // this request in.
        var selection = await SelectBackendAsync(context, modelConfig, requestInfo, callerTenantId, callerApiKeyId)
            .ConfigureAwait(false);
        if (selection.Backend is not { } backend)
        {
            await RejectAtAdmissionAsync(
                context,
                modelConfig.Id,
                requestInfo.Stream,
                selection.Error!,
                outcome: selection.Outcome!).ConfigureAwait(false);
            return;
        }

        var backendKey = ModelBackends.Key(modelConfig.Id, backend.Name);

        // Who is paid for this request: the model's provider, or the standby's when a standby took
        // it. Told to the caller on every response, so being carried by a standby is visible.
        var servedProvider = ModelProviders.ResolveBackend(modelConfig, backend);
        context.Response.Headers[GatewayHeaders.Provider] = servedProvider;
        if (selection.OnStandby)
        {
            context.Response.Headers[GatewayHeaders.Standby] = "true";
            _metricsCollector.RecordStandbyServed(modelConfig.Id, servedProvider);
            _logger.LogInformation(
                "Model {ModelId}: no primary backend could take the request; forwarding to standby {Backend} ({Provider})",
                modelConfig.Id,
                backend.Name,
                servedProvider);
        }

        // Admission consumed the breaker's half-open probe permit. The lease guarantees it is released
        // on every exit path — including thrown exceptions — so the breaker cannot wedge in HalfOpen.
        //
        // The outcome is recorded against the caller: the key, or the client address block for
        // keyless traffic. The breaker opens for every caller of the model, so it wants failures
        // from more than one of them while more than one is using it.
        var rateSubject = RateLimitPartition.ResolveSubject(context);
        using var circuitLease = new CircuitBreakerProbeLease(
            _circuitBreakers,
            backendKey,
            rateSubject.ApiKeyId ?? rateSubject.PartitionKey);

        var bulkheadLease = await _bulkhead.TryAcquireAsync(modelConfig.Id, context.RequestAborted).ConfigureAwait(false);
        if (bulkheadLease is null)
        {
            // Gateway-side saturation, not backend ill health: abandon the probe rather than
            // recording a failure that would trip the breaker on a healthy backend.
            //
            // Reported as a concurrency limit (429 + Retry-After), not as UpstreamError. A 502
            // backend_error told clients the model itself had failed, so OpenAI-compatible routers
            // marked it down and failed over — when the correct signal is simply "retry shortly".
            await RejectAtAdmissionAsync(
                context,
                modelConfig.Id,
                requestInfo.Stream,
                _errors.Write(
                    GatewayErrorCode.ConcurrencyLimitExceeded,
                    message: "Too many concurrent requests for this model. Retry shortly."),
                outcome: "bulkhead_full",
                retryAfterSeconds: 1).ConfigureAwait(false);
            return;
        }

        using (bulkheadLease)
        {
            // Counted from here, after the wait for a slot, until the response is done: this is
            // the number the next request's choice of backend reads, and the number that tells an
            // operator a stopped backend has emptied.
            using var backendInFlight = _backendInFlight.Enter(modelConfig.Id, backend.Name);

            var ratePartitionKey = rateSubject.PartitionKey;

            // The stream-concurrency cap is part of rate limiting, so the same master switch governs it;
            // otherwise "rate limiting off" would still throttle streaming requests.
            var rateLimitingEnabled = _rateLimitPlanResolver?.IsEnabled() ?? _rateLimitPolicyResolver.IsEnabled();

            var streamLease = RateLimitSlotLease.Empty;
            if (requestInfo.Stream && rateLimitingEnabled)
            {
                // Every scope that caps concurrency is taken at once, in one place, so a model's
                // slots and a tenant's slots cannot be admitted independently and then disagree. The
                // model id is the canonical one, which is what per-model rules are keyed on.
                var streamAcquire = AcquireStreamSlots(rateSubject, modelConfig.Id, out streamLease);
                if (!streamAcquire.IsAcquired)
                {
                    // Client-tier concurrency limit, not a backend signal. It is governed by the
                    // rate-limit master switch, so it is counted as a rate-limit rejection too —
                    // otherwise the console's "Rate-limited" stat silently omitted stream caps.
                    var scopeLabel = streamAcquire.Scope?.ToLabel() ?? "tenant";
                    _metricsCollector.RecordRateLimitRejection(
                        "stream_concurrency:" + scopeLabel,
                        ratePartitionKey,
                        modelConfig.Id);
                    _metricsCollector.RecordRateLimitDecision(
                        CallerOf(context),
                        modelConfig.Id,
                        streamAcquire.Scope,
                        RateLimitControl.Concurrency,
                        admitted: false);
                    InferenceAccessLog.SetLimitDecision(context, admitted: false, streamAcquire.Scope);
                    // Zero rpm on purpose, not a missing value: this decision came from a slot
                    // count, and a slot count is not a per-minute rate. Passing streamAcquire.Limit
                    // here wrote MaxConcurrentStreams into the report's rpm columns, and because a
                    // key's tier is last-writer-wins, one streaming refusal corrupted that key's
                    // "usage against limit" until the next rate decision overwrote it.
                    _rateLimitUsage?.Record(new RateLimitUsageEvent(
                        ratePartitionKey,
                        rateSubject.ApiKeyId,
                        modelConfig.Id,
                        Admitted: false,
                        streamAcquire.Scope,
                        RateLimitControl.Concurrency,
                        streamAcquire.ConfiguredRpm,
                        streamAcquire.EffectiveRpm));

                    // The same treatment a rate refusal gets in RateLimitMiddleware, which this used
                    // to skip on both counts: the partition's escalating backoff was recorded here
                    // and then never read, so a client pinned on a concurrency cap was told to
                    // retry in one second however long it had been pinned; and the answer carried
                    // none of the budget headers, so it could not see which cap had refused it or
                    // how many slots were left. Escalation is skipped for the shared scopes for the
                    // same reason it is there — see RateLimitScopeExtensions.IsCallerScoped.
                    var refusedAt = DateTimeOffset.UtcNow;
                    var retryAfter = streamAcquire.RetryAfterSeconds ?? 1;
                    if (_rateLimitGovernor is not null && (streamAcquire.Scope?.IsCallerScoped() ?? true))
                    {
                        _rateLimitGovernor.RecordOutcome(ratePartitionKey, admitted: false, refusedAt);
                        retryAfter = _rateLimitGovernor.GetRetryAfterSeconds(
                            ratePartitionKey,
                            retryAfter,
                            refusedAt);
                    }

                    RateLimitResponseHeaders.Write(context, streamAcquire, refused: true);
                    await RejectAtAdmissionAsync(
                        context,
                        modelConfig.Id,
                        requestInfo.Stream,
                        _errors.Write(GatewayErrorCode.ConcurrencyLimitExceeded),
                        outcome: "stream_concurrency",
                        retryAfterSeconds: retryAfter).ConfigureAwait(false);
                    return;
                }
            }

            // Past every admission gate: this is the first point at which the request is genuinely
            // admitted, so it is the point that clears the partition's retry backoff.
            _rateLimitGovernor?.RecordOutcome(ratePartitionKey, admitted: true, DateTimeOffset.UtcNow);

            // Declared outside the try so the catch below can mark it failed. Every exception that
            // escapes the forward would otherwise dispose a scope with no outcome, which the tracker
            // reads as success — a 502 to the client that never reached the Overview error count.
            IInferenceRequestScope? inferenceScope = null;
            try
            {
                context.Request.Body.Position = 0;

            var started = DateTimeOffset.UtcNow;
            context.Items[InferenceForwardingContextKeys.StartedUtc] = started;
            context.Items[InferenceForwardingContextKeys.ModelId] = modelConfig.Id;
            var scopeTenantId = context.Items.TryGetValue(TenantContextKeys.HttpContextItemKey, out var scopeTenantValue) &&
                                scopeTenantValue is TenantContext scopeTenant
                ? scopeTenant.TenantId
                : null;
            inferenceScope = _requestTracker.BeginInferenceRequest(
                modelConfig.Id,
                requestInfo.Stream,
                scopeTenantId,
                CallerOf(context));
            inferenceScope.SetProvider(servedProvider);

            var requestId = ResolveRequestId(context);

            // Published before the forward, not after it: this is what puts a running inference on
            // the dashboard while it runs instead of only once it has already finished.
            TenantContext? usageTenant = null;
            if (context.Items.TryGetValue(TenantContextKeys.HttpContextItemKey, out var usageTenantValue) &&
                usageTenantValue is TenantContext tenantContextForUsage)
            {
                usageTenant = tenantContextForUsage;
            }

            using var inFlightLease = PublishInFlight(
                context, requestId, modelConfig.Id, started, requestInfo.Stream, usageTenant?.CostCenter);

            // Length of the body actually forwarded. Used only to approximate prompt tokens when a
            // stream ends before its authoritative usage frame — the upstream read and charged for
            // the whole prompt regardless of how the response ended.
            var requestBodyBytes = context.Request.Body.CanSeek ? context.Request.Body.Length : 0L;

            // The same partition key the quota middleware checked this request under. Committing the
            // usage anywhere else means the next check never sees it.
            var usageCapture = new InferenceUsageCapture(
                _usageRecorder,
                _metricsCollector,
                modelConfig.Id,
                requestId,
                started,
                usageTenant,
                requestBodyBytes,
                quotaPartition: ratePartitionKey,
                provider: servedProvider);

            var upstreamBearerToken = _upstreamBearerTokenResolver.ResolveBearerToken(backend.UpstreamAuth);
            if (backend.UpstreamAuth is not null && string.IsNullOrWhiteSpace(upstreamBearerToken))
            {
                // Permanent gateway misconfiguration. Recording it as a backend failure would trip the
                // breaker on every request and mask the real cause behind an opaque circuit_open error.
                _logger.LogError(
                    "Upstream auth token not configured for model {ModelId}; check UpstreamAuth configuration",
                    modelConfig.Id);
                inferenceScope.SetOutcome(false, "upstream_error");
                _metricsCollector.RecordForwardAttempt(modelConfig.Id, "upstream_error");
                await context.WriteGatewayErrorAsync(
                    _errors.Write(
                        GatewayErrorCode.UpstreamError,
                        message: "Upstream auth token not configured for this model."),
                    context.RequestAborted).ConfigureAwait(false);
                RecordRecentRequest(
                    context,
                    modelConfig.Id,
                    started,
                    requestInfo.Stream,
                    success: false,
                    outcome: "upstream_auth_missing",
                    upstreamUrl: backend.Url,
                    usage: usageCapture);
                return;
            }

            // Reserve the estimated max cost against hard-stop budgets before forwarding, so concurrent
            // requests (whose actual cost is unknown until the response returns) cannot collectively
            // overshoot a hard cap.
            // The prompt side is reserved too: for long-context traffic the input is the dominant
            // cost, and pricing only max_tokens let concurrent large-prompt requests overshoot a hard
            // cap by orders of magnitude while each reserved a few thousand output tokens.
            var budgetReservation = await _budgetEnforcement.TryReserveAsync(
                usageTenant?.TenantId,
                requestId,
                modelConfig.Id,
                requestInfo.MaxTokens,
                requestBodyBytes,
                servedProvider,
                context.RequestAborted).ConfigureAwait(false);
            if (!budgetReservation.IsAllowed)
            {
                inferenceScope.SetOutcome(false, "budget_exceeded");
                _metricsCollector.RecordForwardAttempt(modelConfig.Id, "budget_exceeded");
                _metricsCollector.RecordBudgetRejection(usageTenant?.TenantId, budgetReservation.BudgetName, modelConfig.Id);
                await context.WriteGatewayErrorAsync(
                    _errors.Write(GatewayErrorCode.QuotaExceeded),
                    context.RequestAborted).ConfigureAwait(false);

                // SetOutcome above already counted this as an error. Without recording it here too,
                // the dashboard counter climbs while the feed and the Errors tab show nothing —
                // the operator sees a number with no way to find out what it refers to.
                RecordRecentRequest(
                    context,
                    modelConfig.Id,
                    started,
                    requestInfo.Stream,
                    success: false,
                    outcome: "budget_exceeded",
                    upstreamUrl: backend.Url,
                    usage: usageCapture);
                return;
            }

            // The monthly cap on a paid provider, per key and per tenant. Reserved like the budget
            // above and for the same reason; it is the last gate because it is the only one that
            // has to be handed back if a later one refuses.
            if (ModelProviders.IsPaid(servedProvider) &&
                callerTenantId is { } capTenantId && callerApiKeyId is { } capApiKeyId)
            {
                var spend = await _spendCaps.TryReserveAsync(
                    capTenantId,
                    capApiKeyId,
                    servedProvider,
                    requestId,
                    modelConfig.Id,
                    requestInfo.MaxTokens,
                    requestBodyBytes,
                    context.RequestAborted).ConfigureAwait(false);
                if (!spend.IsAllowed)
                {
                    _budgetEnforcement.ReleaseReservation(requestId);

                    var capScope = spend.Scope == ProviderSpendScope.ApiKey ? "api_key" : "tenant";
                    var providerName = ModelProviders.DisplayName(servedProvider);
                    inferenceScope.SetOutcome(false, "spend_cap_reached");
                    _metricsCollector.RecordForwardAttempt(modelConfig.Id, "spend_cap_reached");
                    _metricsCollector.RecordSpendCapRejection(
                        servedProvider,
                        capScope,
                        usageTenant?.TenantSlug,
                        modelConfig.Id);
                    await context.WriteGatewayErrorAsync(
                        _errors.Write(
                            GatewayErrorCode.SpendCapReached,
                            message: spend.Scope == ProviderSpendScope.ApiKey
                                ? $"This API key has used its monthly {providerName} cap of {spend.CapUsd:0.##} USD. A tenant admin can raise it."
                                : $"This tenant has used its monthly {providerName} cap of {spend.CapUsd:0.##} USD. An operator can raise it.",
                            details: new Dictionary<string, object>
                            {
                                ["reason"] = "spend_cap_reached",
                                ["provider"] = servedProvider,
                                ["scope"] = capScope,
                            }),
                        context.RequestAborted).ConfigureAwait(false);
                    RecordRecentRequest(
                        context,
                        modelConfig.Id,
                        started,
                        requestInfo.Stream,
                        success: false,
                        outcome: "spend_cap_reached",
                        upstreamUrl: backend.Url,
                        usage: usageCapture);
                    return;
                }
            }

            // Only a successful forward produces a usage event, and only that event's persistence
            // settles the reservation. Every other exit — including a thrown exception — must hand
            // the headroom back here; relying on the TTL meant a failed request held budget for the
            // full TTL, and any request outliving the TTL had its reservation swept while still
            // in flight, which is exactly the overshoot the ledger exists to prevent.
            var settledByUsage = false;

            // Whether the upstream answered with a success status, for the token limit: a served
            // response that could not be measured keeps its reservation as the charge.
            var servedByUpstream = false;
            try
            {
            var servedDefinition = ModelProviders.TryGetPaid(servedProvider, out var paidDefinition) ? paidDefinition : null;
            var transformer = new StreamingHttpTransformer(
                requestInfo.Stream,
                requestInfo.Model,
                // The name this backend knows the model by, when it is not the model's own id.
                string.IsNullOrWhiteSpace(backend.UpstreamModel) ? modelConfig.Id : backend.UpstreamModel,
                usageCapture,
                stripClientAuthHeaders: true,
                upstreamBearerToken: upstreamBearerToken,
                // Where the client's model value sits in the body. Lets an alias be swapped for the
                // canonical id by splicing bytes rather than rebuilding the document.
                modelValueRange: requestInfo.ModelValueRange)
            {
                // A paid provider charges for the answer whether or not the client stays for it, so
                // the gateway stays for it instead and records what was charged.
                CompleteUpstreamOnClientCancel = ModelProviders.IsPaid(servedProvider),
                RequestInfo = requestInfo,

                // Usage and price as the provider bills them: a stream asks for its usage where the
                // provider sends it only when asked, and a client's service tier is pinned to the one
                // the price list holds where the provider does not report its charge.
                RequestStreamUsage = servedDefinition?.StreamUsageOnRequest == true &&
                    ClassifyRoute(context.Request.Path) is "chat" or "completions",
                PinnedServiceTier = servedDefinition?.PinnedServiceTier,
            };

            // The forwarder owns both deadlines (header timeout vs response-idle timeout) and reports
            // which one fired through its return value, so RequestAborted is left as the client's
            // own token. Overwriting it with a total-duration deadline is what used to truncate
            // healthy long streams and attribute the truncation to the backend.
            //
            // The header deadline is widened in proportion to the prompt actually being forwarded: a
            // fixed one made a large-context request time out purely because the backend was still
            // reading its prompt, and the breaker then counted that against a backend that was
            // working correctly.
            var upstreamStarted = Stopwatch.GetTimestamp();
            var error = await _forwarder.SendAsync(
                context,
                backend.Url,
                upstreamBearerToken,
                transformer,
                requestInfo.Stream,
                _forwardTimeouts.ForRequestBody(requestBodyBytes),
                context.RequestAborted).ConfigureAwait(false);
            _metricsCollector.RecordUpstreamDuration(
                modelConfig.Id,
                Stopwatch.GetElapsedTime(upstreamStarted).TotalSeconds);

            if (error == ForwarderError.None)
            {
                var upstreamStatus = context.Response.StatusCode;
                InferenceAccessLog.SetUpstreamStatus(context, upstreamStatus);

                // 402 is an account out of credit and 401 a key the provider no longer accepts.
                // Both are answers, so the breaker does not count them and the model stays "up";
                // this counter is what tells an operator every caller is being refused.
                if (ModelProviders.IsPaid(servedProvider) &&
                    upstreamStatus is StatusCodes.Status401Unauthorized or StatusCodes.Status402PaymentRequired)
                {
                    _metricsCollector.RecordProviderAccountRefusal(servedProvider, upstreamStatus);
                }

                // Usage capture runs during response disposal, inside SendAsync, so by now we know
                // whether an event was actually enqueued. If it was not (unparseable or absent usage)
                // nothing downstream will settle the reservation, so the finally must release it.
                settledByUsage = usageCapture.HasEnqueuedUsage;
                servedByUpstream = upstreamStatus < StatusCodes.Status400BadRequest;

                // "Forwarded" is not "succeeded". The forwarder reports None for any proxied status
                // code, and a backend that degrades into fast 5xx responses is exactly as unhealthy
                // as one that times out — counting those as breaker successes closed a half-open
                // breaker on the first 500 and kept it closed forever, while metrics and the recent
                // requests feed reported the failure as success.
                //
                // Only the statuses that say the backend, or the hop in front of it, is not serving
                // count against the breaker: 502, 503 and 504. A 500 is an answer, and model servers
                // give it for one caller's request shape (a tool schema they cannot compile) while
                // serving everyone else, so counting it let one caller's failing requests close a
                // model for every team. It is left as no outcome rather than a success: a half-open
                // breaker is not closed by it either.
                if (upstreamStatus >= StatusCodes.Status500InternalServerError)
                {
                    if (IsBackendUnavailable(upstreamStatus))
                    {
                        circuitLease.RecordFailure();
                    }

                    inferenceScope.SetOutcome(false, "upstream_5xx");
                    _metricsCollector.RecordForwardAttempt(modelConfig.Id, "upstream_5xx", upstreamStatus);
                    RecordRecentRequest(
                        context,
                        modelConfig.Id,
                        started,
                        requestInfo.Stream,
                        success: false,
                        outcome: "upstream_5xx",
                        upstreamUrl: backend.Url,
                    usage: usageCapture);
                    return;
                }

                // A proxied 4xx splits the two questions the outcome used to answer at once. The
                // backend answered, so it stays healthy as far as the breaker is concerned — but the
                // client got an error, so it counts as one on the dashboard. Conflating them meant a
                // model rejecting every call with 400 reported "0 errors, 0.00% error rate" beside a
                // feed full of red rows.
                var clientError = upstreamStatus >= StatusCodes.Status400BadRequest;
                circuitLease.RecordSuccess();
                if (clientError)
                {
                    inferenceScope.SetOutcome(false, "upstream_4xx");
                    _metricsCollector.RecordForwardAttempt(modelConfig.Id, "upstream_4xx", upstreamStatus);
                }
                else
                {
                    inferenceScope.SetOutcome(true);
                    _metricsCollector.RecordForwardAttempt(modelConfig.Id, "success", upstreamStatus);
                }

                RecordRecentRequest(
                    context,
                    modelConfig.Id,
                    started,
                    requestInfo.Stream,
                    success: !clientError,
                    outcome: clientError ? "upstream_4xx" : "success",
                    upstreamUrl: backend.Url,
                    usage: usageCapture);
                return;
            }

            if (error == ForwarderError.RequestTimedOut)
            {
                // Headers never arrived: the backend failed to respond at all, a real health signal.
                circuitLease.RecordFailure();
                inferenceScope.SetOutcome(false, "upstream_timeout");
                _metricsCollector.RecordForwardAttempt(modelConfig.Id, "upstream_timeout");
                if (!context.Response.HasStarted)
                {
                    await context.WriteGatewayErrorAsync(
                        _errors.Write(
                            GatewayErrorCode.UpstreamError,
                            message: "Request timed out while forwarding to backend."),
                        CancellationToken.None).ConfigureAwait(false);
                }

                RecordRecentRequest(
                    context,
                    modelConfig.Id,
                    started,
                    requestInfo.Stream,
                    success: false,
                    outcome: "upstream_timeout",
                    upstreamUrl: backend.Url,
                    usage: usageCapture);
                return;
            }

            if (error == ForwarderError.ResponseBodyCanceled)
            {
                // Whether anything reached the client separates two different faults, and they are
                // judged differently.
                //
                // Zero bytes means the upstream accepted the request, returned headers, and then
                // produced nothing for the whole first-byte allowance — by default five minutes plus
                // a minute per megabyte of prompt. For an SSE backend those headers are written on
                // admission, before scheduling and before prefill, so they are not evidence of a
                // working backend; nothing was generated. A hung worker, a deadlocked scheduler or a
                // dead GPU process looks exactly like this, and it IS backend ill health, so it is
                // counted. The breaker needs both an absolute failure count and a failure ratio over
                // its window, so a backend that is merely busy — and still answering other callers —
                // does not trip on these.
                //
                // A stall after N bytes is a generation that demonstrably started and then stopped.
                // The backend proved it was producing, so that stays inconclusive: abandon the probe
                // (via lease dispose) rather than counting a failure.
                var responseBytesForwarded = ResolveResponseBytesForwarded(context);
                var noFirstByte = responseBytesForwarded == 0;
                var stallOutcome = noFirstByte ? "upstream_first_byte_timeout" : "stream_idle_timeout";
                if (noFirstByte)
                {
                    circuitLease.RecordFailure();
                }

                _logger.LogWarning(
                    "Response body for model {ModelId} stalled past its allowance after {ElapsedMs}ms with {ResponseBytesForwarded} bytes forwarded to the client",
                    modelConfig.Id,
                    (DateTimeOffset.UtcNow - started).TotalMilliseconds,
                    responseBytesForwarded);
                inferenceScope.SetOutcome(false, stallOutcome);
                _metricsCollector.RecordForwardAttempt(modelConfig.Id, stallOutcome);

                // A streaming response has already flushed its first bytes, but a non-streaming one
                // can stall before a single byte is written. Without this the client received the
                // upstream's status code and an empty body rather than an error it can act on.
                if (!context.Response.HasStarted)
                {
                    await context.WriteGatewayErrorAsync(
                        _errors.Write(
                            GatewayErrorCode.UpstreamError,
                            message: noFirstByte
                                ? "Backend returned response headers but never started sending the response body."
                                : "Backend stopped sending the response body."),
                        CancellationToken.None).ConfigureAwait(false);
                }

                RecordRecentRequest(
                    context,
                    modelConfig.Id,
                    started,
                    requestInfo.Stream,
                    success: false,
                    outcome: stallOutcome,
                    upstreamUrl: backend.Url,
                    usage: usageCapture);
                return;
            }

            if (error == ForwarderError.ResponseBodyClient)
            {
                // The client held the connection open and stopped reading, so the gateway gave up
                // writing to it. Backpressure had reached the upstream read, which means the client —
                // not the backend — was holding the upstream connection, the bulkhead slot and the
                // budget reservation. Nothing here is evidence about the backend, so the probe is
                // abandoned rather than failed.
                //
                // Kept distinct from client_canceled: a clean disconnect needs no operator attention
                // and is not recorded, while a client that stalls mid-response is a fault worth
                // seeing — it is how a misbehaving consumer starves a model's concurrency.
                _logger.LogWarning(
                    "Client stopped reading the response body for model {ModelId} after {ElapsedMs}ms",
                    modelConfig.Id,
                    (DateTimeOffset.UtcNow - started).TotalMilliseconds);
                inferenceScope.SetOutcome(false, "client_write_timeout");
                _metricsCollector.RecordForwardAttempt(modelConfig.Id, "client_write_timeout");

                // A client that is not reading will not read an error body either, but a non-streaming
                // response has not started, and leaving it to complete as a bare 200 with no body
                // would record a success for a request that failed.
                if (!context.Response.HasStarted)
                {
                    await context.WriteGatewayErrorAsync(
                        _errors.Write(
                            GatewayErrorCode.UpstreamError,
                            message: "The client stopped reading the response body."),
                        CancellationToken.None).ConfigureAwait(false);
                }

                RecordRecentRequest(
                    context,
                    modelConfig.Id,
                    started,
                    requestInfo.Stream,
                    success: false,
                    outcome: "client_write_timeout",
                    upstreamUrl: backend.Url,
                    usage: usageCapture);
                return;
            }

            if (error == ForwarderError.ResponseBodyDestination)
            {
                // The upstream answered and then broke the body off — connection reset, premature
                // EOF, bad framing. That is the backend's failure, not the client's: it counts against
                // the breaker and shows up in the error store like any other upstream error. Before
                // this was distinguished it was reported as client_canceled, which hid a flapping
                // backend from the breaker and the operator, and a non-streaming client got the
                // upstream's 200 with a truncated body instead of an error it could act on.
                circuitLease.RecordFailure();
                inferenceScope.SetOutcome(false, "upstream_body_error");
                _metricsCollector.RecordForwardAttempt(modelConfig.Id, "upstream_body_error");
                if (!context.Response.HasStarted)
                {
                    await context.WriteGatewayErrorAsync(
                        _errors.Write(
                            GatewayErrorCode.UpstreamError,
                            message: "Backend connection failed while sending the response body."),
                        CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning(
                        "Upstream response body for model {ModelId} failed after {ElapsedMs}ms with the response already started; the client received a truncated body",
                        modelConfig.Id,
                        (DateTimeOffset.UtcNow - started).TotalMilliseconds);
                }

                RecordRecentRequest(
                    context,
                    modelConfig.Id,
                    started,
                    requestInfo.Stream,
                    success: false,
                    outcome: "upstream_body_error",
                    upstreamUrl: backend.Url,
                    usage: usageCapture);
                return;
            }

            if (error == ForwarderError.RequestCanceled)
            {
                // The client hung up. This is not evidence the backend is unhealthy, and counting it
                // would let a burst of disconnects trip the breaker on a perfectly good backend.
                // Leaving the lease unrecorded abandons the probe on dispose.
                inferenceScope.SetClientCanceled();
                _metricsCollector.RecordForwardAttempt(modelConfig.Id, "client_canceled");

                // A paid upstream was read to its end after the client left, so its usage is on the
                // way to the ledger and settles the reservations, as a served request's does.
                settledByUsage = usageCapture.HasEnqueuedUsage;
                servedByUpstream = usageCapture.HasEnqueuedUsage && context.Response.StatusCode < StatusCodes.Status400BadRequest;
                RecordRecentRequest(
                    context,
                    modelConfig.Id,
                    started,
                    requestInfo.Stream,
                    success: false,
                    outcome: "client_canceled",
                    upstreamUrl: backend.Url,
                    usage: usageCapture);
                return;
            }

            circuitLease.RecordFailure();
            inferenceScope.SetOutcome(false, "upstream_error");
            _metricsCollector.RecordForwardAttempt(modelConfig.Id, "upstream_error");
            if (!context.Response.HasStarted)
            {
                _logger.LogWarning("Forwarder error {Error} for model {ModelId}", error, modelConfig.Id);
                await context.WriteGatewayErrorAsync(
                    _errors.Write(GatewayErrorCode.UpstreamError),
                    CancellationToken.None).ConfigureAwait(false);
            }

            RecordRecentRequest(
                context,
                modelConfig.Id,
                started,
                requestInfo.Stream,
                success: false,
                outcome: "upstream_error",
                upstreamUrl: backend.Url,
                    usage: usageCapture);
            }
            finally
            {
                // Read by RateLimitMiddleware once this returns, to settle a token reservation
                // against what the request really used. A stream the client cut off reports its
                // estimate here, the same one it is billed from.
                RateLimitTokenUsage.Report(context, usageCapture.CapturedUsage, servedByUpstream);

                if (!settledByUsage)
                {
                    // Idempotent: releasing a request id the ledger no longer holds is a no-op, so
                    // this is safe even if usage settlement won the race.
                    _budgetEnforcement.ReleaseReservation(requestId);
                    _spendCaps.Release(requestId);
                }
            }
            }
            catch (Exception) when (inferenceScope is not null)
            {
                if (context.RequestAborted.IsCancellationRequested)
                {
                    inferenceScope.SetClientCanceled();
                }
                else
                {
                    inferenceScope.SetOutcome(false, "unhandled");
                }

                throw;
            }
            finally
            {
                inferenceScope?.Dispose();
                if (streamLease.Count > 0)
                {
                    _rateLimitStore.ReleaseStreamSlots(streamLease);
                }
            }
        }
    }

    /// <summary>
    /// Takes a concurrency slot in every scope that caps concurrency for this caller and model.
    /// </summary>
    /// <remarks>
    /// Falls back to the tenant tier alone when no plan resolver is wired — the behaviour before
    /// scoped rules existed, and what the middleware's hand-built test instances get.
    /// </remarks>
    /// <summary>
    /// Picks the backend this request is forwarded to and enters its circuit breaker, or says why
    /// none can take it.
    /// </summary>
    /// <remarks>
    /// The order comes from <see cref="BackendSelector"/>; this walks it until a breaker admits the
    /// request. An open breaker refuses and the next backend is tried, and a breaker whose break has
    /// elapsed admits this request as its probe — which is how a backend that failed comes back
    /// without anything else having to notice. Primary backends come first; a standby is tried
    /// only when none of them took the request.
    /// </remarks>
    private async ValueTask<BackendSelection> SelectBackendAsync(
        HttpContext context,
        ModelConfig model,
        InferenceRequestInfo request,
        Guid? tenantId,
        Guid? apiKeyId)
    {
        var serving = model.GetBackends().Where(b => b.IsServing()).ToList();
        if (serving.Count == 0)
        {
            return BackendSelection.Refused(
                _errors.Write(
                    GatewayErrorCode.BackendUnhealthy,
                    message: $"Model '{model.Id}' has no backend in service."),
                "backend_unhealthy");
        }

        // Primary backends share the traffic. A standby is asked only when none of them can take
        // the request, so a paid standby costs nothing while the model's own hardware is serving.
        // A model with nothing but standbys treats them as its backends.
        var primaries = serving.Where(b => !b.IsStandby()).ToList();
        var standbys = primaries.Count == 0 ? [] : serving.Where(b => b.IsStandby()).ToList();
        if (primaries.Count == 0)
        {
            primaries = serving;
        }

        var chosen = TryEnterOneOf(context, model, request, primaries, out var healthy);
        if (chosen is not null)
        {
            return BackendSelection.Chosen(chosen, onStandby: false);
        }

        if (standbys.Count > 0)
        {
            var permitted = new List<ModelBackendConfig>(standbys.Count);
            foreach (var standby in standbys)
            {
                if (await MayUseStandbyAsync(context, model, standby, tenantId, apiKeyId).ConfigureAwait(false))
                {
                    permitted.Add(standby);
                }
            }

            chosen = TryEnterOneOf(context, model, request, permitted, out _);
            if (chosen is not null)
            {
                return BackendSelection.Chosen(chosen, onStandby: true);
            }
        }

        // No standby took it either, or the caller may not use one: the answer is the one the
        // model's own backends give, exactly as it was before standbys existed.
        if (healthy.Count == 0)
        {
            return BackendSelection.Refused(
                _errors.Write(
                    GatewayErrorCode.BackendUnhealthy,
                    message: $"Backend for model '{model.Id}' is unhealthy."),
                "backend_unhealthy");
        }

        _logger.LogWarning(
            "Rejected request for model {ModelId}: the circuit breaker of every healthy backend refused it ({Backends})",
            model.Id,
            string.Join(
                ", ",
                healthy.Select(b =>
                    b.Name + "=" + ModelCircuitBreakerRegistry.ToStateLabel(
                        _circuitBreakers.GetBreaker(ModelBackends.Key(model.Id, b.Name)).State))));
        return BackendSelection.Refused(_errors.Write(GatewayErrorCode.CircuitOpen), "circuit_open");
    }

    /// <summary>
    /// Enters the circuit breaker of the first of <paramref name="backends"/> that is healthy and
    /// whose breaker admits the request, in the order <see cref="BackendSelector"/> gives.
    /// </summary>
    /// <param name="healthy">The backends that were healthy, whether or not a breaker admitted one.</param>
    private ModelBackendConfig? TryEnterOneOf(
        HttpContext context,
        ModelConfig model,
        InferenceRequestInfo request,
        IReadOnlyList<ModelBackendConfig> backends,
        out List<ModelBackendConfig> healthy)
    {
        healthy = [];
        var available = new List<BackendCandidate>(backends.Count);
        foreach (var candidate in backends)
        {
            if (_healthStore.IsBackendHealthy(model.Id, candidate.Name))
            {
                healthy.Add(candidate);
                available.Add(new BackendCandidate(
                    candidate,
                    _backendInFlight.GetInFlight(model.Id, candidate.Name)));
            }
        }

        if (available.Count == 0)
        {
            return null;
        }

        // Only a model with a choice to make pays for the estimate.
        var estimatedTokens = available.Count > 1 ? EstimateContextTokens(context, request) : 0;

        foreach (var candidate in BackendSelector.Order(available, estimatedTokens))
        {
            if (_circuitBreakers.TryEnter(ModelBackends.Key(model.Id, candidate.Name)))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether this caller may be carried by <paramref name="standby"/>. A self-hosted standby is
    /// for everyone the model is for. A paid one is only for a key that is opened for its provider,
    /// with its tenant: being on the model's list was never a decision to spend at a provider.
    /// </summary>
    private async ValueTask<bool> MayUseStandbyAsync(
        HttpContext context,
        ModelConfig model,
        ModelBackendConfig standby,
        Guid? tenantId,
        Guid? apiKeyId)
    {
        var provider = ModelProviders.ResolveBackend(model, standby);
        if (!ModelProviders.IsPaid(provider) || !_authState.IsAuthenticationRequired)
        {
            // A gateway that asks for no key has no switches to be opened by.
            return true;
        }

        if (tenantId is not { } tenant || apiKeyId is not { } key)
        {
            // Keyless traffic to a public model: there is nobody to charge.
            return false;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var modelGrants = scope.ServiceProvider.GetRequiredService<IModelGrantService>();
        return await modelGrants.IsProviderOpenAsync(tenant, key, provider, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>The backend a request goes to, or the answer when none can take it.</summary>
    private readonly record struct BackendSelection(
        ModelBackendConfig? Backend,
        bool OnStandby,
        WrittenErrorResponse? Error,
        string? Outcome)
    {
        public static BackendSelection Chosen(ModelBackendConfig backend, bool onStandby) =>
            new(backend, onStandby, null, null);

        public static BackendSelection Refused(WrittenErrorResponse error, string outcome) =>
            new(null, false, error, outcome);
    }

    /// <summary>
    /// The context a request needs, in tokens: its prompt, approximated from the body's size as the
    /// rate limiter and the usage capture do, plus the completion it asked for.
    /// </summary>
    private static long EstimateContextTokens(HttpContext context, InferenceRequestInfo request)
    {
        var body = context.Request.Body;
        var bodyBytes = body.CanSeek ? body.Length : context.Request.ContentLength ?? 0;
        return UsageEventFactory.EstimatePromptTokens(bodyBytes) + (request.MaxTokens is > 0 ? request.MaxTokens.Value : 0);
    }

    private RateLimitAcquireResult AcquireStreamSlots(
        in RateLimitSubject subject,
        string modelId,
        out RateLimitSlotLease lease)
    {
        if (_rateLimitPlanResolver is null)
        {
            lease = RateLimitSlotLease.Empty;
            // The tenant tier only: the anonymous tier is applied by the plan resolver, which every
            // real host wires, so this path does not consult it.
            var tier = _rateLimitPolicyResolver.Resolve(subject.PlanSlug, subject.TenantId, subject.TenantSlug);
            var single = _rateLimitStore.TryAcquireStreamSlot(subject.PartitionKey, tier);
            if (single.IsAcquired && tier.EnforcesConcurrency)
            {
                lease = new RateLimitSlotLease([subject.PartitionKey], 1);
            }

            return single;
        }

        var plan = _rateLimitPlanResolver.Resolve(subject, modelId);
        var acquired = _rateLimitStore.TryAcquireStreamSlots(plan.Rules, out lease);

        // Per limit: a stream started under every cap, or one refusal under the cap that was full.
        _rateLimitUsage?.RecordStreamStage(plan.Rules, acquired.IsAcquired ? null : acquired.PartitionKey);
        return acquired;
    }

    /// <summary>
    /// Answers a request the gateway refused before forwarding, and records it everywhere the
    /// console reads from: the request and error totals, the per-model error breakdown, and the
    /// live feed.
    /// </summary>
    /// <remarks>
    /// These four paths (unhealthy backend, open circuit, full bulkhead, exhausted stream slot)
    /// previously reported only to Prometheus, so a saturated gateway rejecting every call still
    /// rendered as "0 errors, 0.00% error rate" on the dashboard. The recording happens after the
    /// response is written so the feed row carries the real status code and error header.
    /// </remarks>
    private async Task RejectAtAdmissionAsync(
        HttpContext context,
        string modelId,
        bool isStreaming,
        WrittenErrorResponse error,
        string outcome,
        int? retryAfterSeconds = null)
    {
        var started = DateTimeOffset.UtcNow;
        _metricsCollector.RecordForwardAttempt(modelId, outcome);

        await context.WriteGatewayErrorAsync(error, context.RequestAborted, retryAfterSeconds)
            .ConfigureAwait(false);

        // Refused before a backend was chosen, so the provider is the model's own.
        var provider = _registry.TryGetModel(modelId, out var refusedModel) && refusedModel is not null
            ? ModelProviders.Resolve(refusedModel)
            : ModelProviders.SelfHosted;
        _requestTracker.RecordRejectedRequest(modelId, outcome, CallerOf(context), provider);
        RecordRecentRequest(context, modelId, started, isStreaming, success: false, outcome: outcome);
    }

    /// <summary>
    /// The error a refused key is answered with. A model missing from the tenant's own list keeps
    /// the code it always had; the other three name their rule.
    /// </summary>
    private static GatewayErrorCode ToErrorCode(ModelAccessRefusal refusal) => refusal switch
    {
        ModelAccessRefusal.ProviderClosedForTenant => GatewayErrorCode.ProviderClosedForTenant,
        ModelAccessRefusal.ProviderClosedForKey => GatewayErrorCode.ProviderClosedForKey,
        ModelAccessRefusal.ModelNotOnKeyList => GatewayErrorCode.ModelNotOnKeyList,
        _ => GatewayErrorCode.InsufficientScope,
    };

    /// <summary>
    /// The proxied statuses the circuit breaker reads as the backend not serving: bad gateway,
    /// service unavailable and gateway timeout.
    /// </summary>
    internal static bool IsBackendUnavailable(int statusCode) =>
        statusCode is StatusCodes.Status502BadGateway
            or StatusCodes.Status503ServiceUnavailable
            or StatusCodes.Status504GatewayTimeout;

    private static MetricCaller CallerOf(HttpContext context) =>
        MetricCaller.From(
            context.Items.TryGetValue(TenantContextKeys.HttpContextItemKey, out var value)
                ? value as TenantContext
                : null);

    /// <summary>
    /// Publishes the request to the live feed the moment forwarding begins, and retires the entry on
    /// every exit path. Until the upstream answers the row carries status 0 and a duration that
    /// grows with each dashboard poll.
    /// </summary>
    private IDisposable PublishInFlight(
        HttpContext context,
        string requestId,
        string modelId,
        DateTimeOffset started,
        bool isStreaming,
        string? costCenter)
    {
        _recentRequestStore.BeginInFlight(new RecentRequestEntry
        {
            RequestId = requestId,
            Method = context.Request.Method,
            Path = context.Request.Path.Value ?? string.Empty,
            ModelId = modelId,
            TenantId = ResolveTenantId(context),
            CostCenter = costCenter,
            StatusCode = 0,
            DurationMs = 0,
            IsStreaming = isStreaming,
            ErrorCode = null,
            TimestampUtc = started,
            IsInFlight = true,
        });

        return new InFlightRequestLease(_recentRequestStore, requestId);
    }

    /// <remarks>
    /// A completed entry for the same request id supersedes the in-flight one inside the store, so
    /// this lease is the guarantee for the paths that never record a completion — a thrown exception
    /// above all. Releasing an id the store no longer holds is a no-op.
    /// </remarks>
    private sealed class InFlightRequestLease(IRecentRequestStore store, string requestId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                store.CompleteInFlight(requestId);
            }
        }
    }

    private static string? ResolveTenantId(HttpContext context) => ResolveTenant(context)?.TenantId;

    private static TenantContext? ResolveTenant(HttpContext context) =>
        context.Items.TryGetValue(TenantContextKeys.HttpContextItemKey, out var tenantValue) &&
        tenantValue is TenantContext tenant
            ? tenant
            : null;

    /// <param name="outcome">
    /// How the request ended, e.g. <c>upstream_5xx</c>. Already computed at every call site for the
    /// metrics counter; passing it here is what lets the Errors tab group by failure shape rather
    /// than by status code alone.
    /// </param>
    /// <param name="upstreamUrl">The model's configured base URL, sanitized before it is stored.</param>
    /// <param name="usage">
    /// The response's usage capture, when the request got far enough to have one. Its token counts
    /// go on the feed row immediately; the costs follow from the billing writer via
    /// <see cref="IRecentRequestStore.AttachUsage"/>, so the row is marked <c>pending</c> only when
    /// an event was actually accepted for pricing.
    /// </param>
    private void RecordRecentRequest(
        HttpContext context,
        string modelId,
        DateTimeOffset started,
        bool isStreaming,
        bool success,
        string? outcome = null,
        string? upstreamUrl = null,
        InferenceUsageCapture? usage = null)
    {
        InferenceAccessLog.SetOutcome(context, outcome ?? (success ? "success" : "error"));

        var requestId = ResolveRequestId(context);
        var tenant = ResolveTenant(context);
        var tenantId = tenant?.TenantId;

        var durationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;

        // Prefer whatever status the gateway actually set — an admission rejection has already
        // written its 429 or 503 even though the body may not have flushed yet, and keying off
        // HasStarted alone reported every one of them to the console as a generic 502.
        var statusCode = context.Response.StatusCode;
        if (!success && !context.Response.HasStarted && statusCode < StatusCodes.Status400BadRequest)
        {
            statusCode = StatusCodes.Status502BadGateway;
        }

        var errorCode = ResolveErrorCode(context);

        var entry = new RecentRequestEntry
        {
            RequestId = requestId,
            Method = context.Request.Method,
            Path = context.Request.Path.Value ?? string.Empty,
            ModelId = modelId,
            TenantId = tenantId,
            CostCenter = tenant?.CostCenter,
            StatusCode = statusCode,
            DurationMs = durationMs,
            IsStreaming = isStreaming,
            TimeToFirstTokenMs = context.Items.TryGetValue(InferenceForwardingContextKeys.TimeToFirstTokenMs, out var ttft) && ttft is double ttftMs
                ? ttftMs
                : null,
            ErrorCode = errorCode,
            TimestampUtc = DateTimeOffset.UtcNow,
        };

        if (usage?.CapturedUsage is UsageEvent captured)
        {
            entry = entry.WithUsage(RecentRequestUsageMapper.FromUsageEvent(
                captured,
                pricingStatus: usage.HasEnqueuedUsage
                    ? RecentRequestUsage.StatusPending
                    : RecentRequestUsage.StatusUnpriced));
        }

        _recentRequestStore.Record(entry);

        if (!success)
        {
            RecordInferenceError(
                context,
                modelId,
                requestId,
                tenantId,
                statusCode,
                durationMs,
                errorCode,
                outcome,
                upstreamUrl,
                isStreaming,
                entry.TimeToFirstTokenMs);
        }
    }

    /// <summary>
    /// Publishes a failed inference to the durable error store, with the model, upstream, outcome
    /// and remediation hint the Errors tab needs to make it actionable.
    /// </summary>
    /// <remarks>
    /// A client hang-up is skipped: the caller walked away, the gateway and the backend both did
    /// their jobs, and filling the error store with disconnects buries the faults an operator is
    /// looking for. The aggregate error counter still counts them, so the Errors tab can legitimately
    /// show fewer errors than the Overview tile.
    /// </remarks>
    private void RecordInferenceError(
        HttpContext context,
        string modelId,
        string requestId,
        string? tenantId,
        int statusCode,
        double durationMs,
        string? errorCode,
        string? outcome,
        string? upstreamUrl,
        bool isStreaming,
        double? timeToFirstTokenMs)
    {
        if (string.Equals(outcome, "client_canceled", StringComparison.Ordinal))
        {
            return;
        }

        var path = context.Request.Path;
        var upstreamException = context.Items.TryGetValue(GatewayErrorContextKeys.UpstreamException, out var stashed)
            ? stashed as Exception
            : null;
        var responseBytesForwarded = ResolveResponseBytesForwarded(context);

        _errorRecorder.Record(new GatewayErrorRecord
        {
            Id = $"err_{Guid.NewGuid():N}",
            Fingerprint = string.Empty,
            OccurredAt = DateTimeOffset.UtcNow,
            Level = ClassifyErrorLevel(statusCode, outcome).ToString(),
            Source = GatewayErrorSourceNames.Proxy,
            Category = nameof(ModelRouterMiddleware),
            EventCode = errorCode ?? outcome,
            Message = BuildErrorMessage(modelId, statusCode, outcome, responseBytesForwarded),
            ExceptionType = upstreamException?.GetType().FullName,
            StackTrace = upstreamException?.ToString(),
            Method = context.Request.Method,
            Path = path.Value,
            RouteKind = ClassifyRoute(path),
            StatusCode = statusCode,
            ModelId = modelId,
            UpstreamTarget = upstreamUrl,
            Outcome = outcome,
            TenantId = tenantId,
            ApiKeyId = ResolveApiKeyId(context),
            RequestId = requestId,
            DurationMs = durationMs,
            IsStreaming = isStreaming,
            TimeToFirstTokenMs = timeToFirstTokenMs,
            ResponseBytesForwarded = responseBytesForwarded,
            UpstreamBodySnippet = context.Items.TryGetValue(
                GatewayErrorContextKeys.UpstreamBodySnippet,
                out var snippet)
                ? snippet as string
                : null,
            // The transport hint ("nothing is listening on the model's URL") beats the status hint
            // whenever there is an exception to read it from.
            Hint = GatewayLogHints.ForException(upstreamException)
                ?? GatewayLogHints.ForUpstreamStatus(statusCode, upstreamUrl, path.Value, modelId),
        });

        // Tells the terminal exception handler this failure is already accounted for.
        context.Items[GatewayErrorContextKeys.ErrorCaptured] = true;
    }

    private static GatewayLogLevel ClassifyErrorLevel(int statusCode, string? outcome) => outcome switch
    {
        // A permanently misconfigured upstream will never recover on its own, unlike a backend
        // having a bad minute — it needs an operator, so it is ranked above an ordinary 5xx.
        "upstream_auth_missing" => GatewayLogLevel.Critical,
        "upstream_4xx" or "budget_exceeded" or "spend_cap_reached" or "backend_unhealthy" or "circuit_open"
            or "bulkhead_full" or "stream_concurrency" or "client_write_timeout" => GatewayLogLevel.Warning,
        // Counted against the breaker, so it is ranked with the other backend failures rather than
        // left at the Warning a 200-status streaming stall would otherwise get.
        "upstream_first_byte_timeout" => GatewayLogLevel.Error,
        _ => statusCode >= StatusCodes.Status500InternalServerError
            ? GatewayLogLevel.Error
            : GatewayLogLevel.Warning,
    };

    /// <summary>Bytes the forwarder delivered to the client, or null when the body phase was never entered.</summary>
    private static long? ResolveResponseBytesForwarded(HttpContext context) =>
        context.Items.TryGetValue(InferenceForwardingContextKeys.ResponseBytesForwarded, out var value) && value is long bytes
            ? bytes
            : null;

    private static string BuildErrorMessage(string modelId, int statusCode, string? outcome, long? responseBytesForwarded) => outcome switch
    {
        "upstream_timeout" => $"Upstream timed out for model '{modelId}'.",
        // Distinct from stream_idle_timeout in both name and message: the fingerprint is taken over
        // the message, and an operator chasing time-to-first-token failures must not find them pooled
        // with mid-stream stalls.
        "upstream_first_byte_timeout" =>
            $"Upstream returned headers but never started sending the response body for model '{modelId}' (time to first token exceeded the allowance).",
        "stream_idle_timeout" => $"Upstream stopped sending the response body for model '{modelId}'.",
        "client_write_timeout" =>
            $"Client stopped reading the response body for model '{modelId}' after {responseBytesForwarded ?? 0} bytes; the gateway gave up writing to it.",
        "upstream_body_error" => $"Upstream connection failed while sending the response body for model '{modelId}'.",
        "backend_unhealthy" => $"Rejected: no healthy backend for model '{modelId}'.",
        "circuit_open" => $"Rejected: circuit breaker open for model '{modelId}'.",
        "bulkhead_full" => $"Rejected: concurrency limit reached for model '{modelId}'.",
        "provider_closed_for_tenant" => $"Rejected: the tenant is not opened for the provider of model '{modelId}'.",
        "provider_closed_for_key" => $"Rejected: the API key is not opened for the provider of model '{modelId}'.",
        "model_not_on_tenant_list" => $"Rejected: model '{modelId}' is not on the tenant's model list.",
        "model_not_on_key_list" => $"Rejected: model '{modelId}' is not on the API key's model list.",
        "stream_concurrency" => $"Rejected: streaming concurrency limit reached for model '{modelId}'.",
        "budget_exceeded" => $"Rejected: budget exhausted for model '{modelId}'.",
        "spend_cap_reached" => $"Rejected: the monthly spend cap for the provider of model '{modelId}' is used up.",
        "upstream_auth_missing" => $"Upstream auth token not configured for model '{modelId}'.",
        _ => $"Upstream returned {statusCode} for model '{modelId}'.",
    };

    private static string? ResolveApiKeyId(HttpContext context) =>
        context.User.FindFirst(GatewayAuthClaims.ApiKeyId)?.Value;

    private static string? ResolveErrorCode(HttpContext context)
    {
        if (!context.Response.Headers.TryGetValue(GatewayHeaders.ErrorCode, out var values))
        {
            return null;
        }

        var code = values.ToString();
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }

    /// <summary>
    /// Resolves the request id once and writes any generated value back into
    /// <see cref="HttpContext.Items"/>, so the usage event, the budget reservation and the
    /// recent-request entry all carry the same id.
    /// </summary>
    /// <remarks>
    /// Minting a fresh GUID per call (the previous behaviour) meant that when RequestIdMiddleware had
    /// not run, the reservation was keyed to an id no release would ever match — it leaked until the
    /// TTL swept it — and the dashboard entry could not be correlated with its billing event.
    /// </remarks>
    private static string ResolveRequestId(HttpContext context)
    {
        if (context.Items.TryGetValue(RequestIdKeys.HttpContextItemKey, out var existing) &&
            existing?.ToString() is { Length: > 0 } requestId)
        {
            return requestId;
        }

        var generated = $"req_{Guid.NewGuid():N}";
        context.Items[RequestIdKeys.HttpContextItemKey] = generated;
        return generated;
    }

    private static string ClassifyRoute(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return "chat";
        }

        if (value.EndsWith("/v1/completions", StringComparison.OrdinalIgnoreCase))
        {
            return "completions";
        }

        if (value.EndsWith("/v1/embeddings", StringComparison.OrdinalIgnoreCase))
        {
            return "embeddings";
        }

        if (value.EndsWith("/v1/rerank", StringComparison.OrdinalIgnoreCase))
        {
            return "rerank";
        }

        return "unknown";
    }
}
