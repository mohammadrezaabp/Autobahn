using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Autobahn.Application.Errors;
using Autobahn.Domain.Identity;
using Autobahn.Application.Observability;
using Autobahn.Proxy.Diagnostics;
using Autobahn.Proxy.Parsing;
using Autobahn.Proxy.Routing;

namespace Autobahn.Proxy.Middleware;

/// <summary>
/// Writes one line per inference request, whoever answered it: the rate limiter, the router, the
/// upstream or authentication.
/// </summary>
/// <remarks>
/// <para>The line is <c>inference_request</c> followed by <c>name=value</c> pairs, so it reads the
/// same in a terminal, in Loki and in a log platform that extracts fields from pairs. The three
/// values an operator or a client chose (tenant, key label, model) are quoted, because a key label
/// may hold spaces. Every value is also a structured property for a sink that keeps them.</para>
///
/// <para>Registered outside the exception handler, so the status logged is the one the handler
/// settled on. <c>tenant</c> and <c>key</c> are the slug and the key's label, the same values the
/// Prometheus series carry: never the secret, its hash or its prefix.</para>
/// </remarks>
public sealed partial class InferenceAccessLogMiddleware(
    RequestDelegate next,
    ILogger<InferenceAccessLogMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!InferenceRouteClassifier.IsRoutableInference(context))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            Write(context, Stopwatch.GetElapsedTime(started));
        }
    }

    private void Write(HttpContext context, TimeSpan elapsed)
    {
        var caller = MetricCaller.From(
            context.Items.TryGetValue(TenantContextKeys.HttpContextItemKey, out var tenant)
                ? tenant as TenantContext
                : null);

        InferenceRequestParseCache.TryGet(context, out var parsed);
        var model = InferenceAccessLog.Read(
            context,
            InferenceAccessLog.ModelKey,
            string.IsNullOrWhiteSpace(parsed?.Model) ? InferenceAccessLog.Unknown : parsed.Value.Model!);

        var statusCode = context.Response.StatusCode;
        var outcome = InferenceAccessLog.Read(context, InferenceAccessLog.OutcomeKey, ResolveOutcome(context, statusCode));
        var limitDecision = InferenceAccessLog.Read(context, InferenceAccessLog.LimitDecisionKey, InferenceAccessLog.None);
        var limitScope = InferenceAccessLog.Read(context, InferenceAccessLog.LimitScopeKey, InferenceAccessLog.None);
        var upstreamStatus =
            context.Items.TryGetValue(InferenceAccessLog.UpstreamStatusKey, out var upstream) && upstream is int upstreamCode
                ? upstreamCode.ToString(CultureInfo.InvariantCulture)
                : InferenceAccessLog.None;
        var requestId = context.Items.TryGetValue(RequestIdKeys.HttpContextItemKey, out var id)
            ? id?.ToString() ?? InferenceAccessLog.Unknown
            : InferenceAccessLog.Unknown;

        // The same facts on the request's span, when one is being recorded.
        if (Activity.Current is { IsAllDataRequested: true } activity)
        {
            activity.SetTag("gateway.request_id", requestId);
            activity.SetTag("gateway.tenant", caller.Tenant);
            activity.SetTag("gateway.key", caller.Key);
            activity.SetTag("gateway.model", model);
            activity.SetTag("gateway.outcome", outcome);
            activity.SetTag("gateway.limit_decision", limitDecision);
            activity.SetTag("gateway.limit_scope", limitScope);
        }

        LogRequest(
            logger,
            requestId,
            Quotable(caller.Tenant),
            Quotable(caller.Key),
            Quotable(model),
            parsed?.Stream ?? false,
            statusCode,
            outcome,
            limitDecision,
            limitScope,
            upstreamStatus,
            (long)elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// A key label and a model name are free text and are written between double quotes, so a
    /// double quote inside one would end the value early for whatever parses the line.
    /// </summary>
    private static string Quotable(string value) =>
        value.Contains('"', StringComparison.Ordinal) ? value.Replace('"', '\'') : value;

    /// <summary>
    /// The outcome of a request nothing on the path named one for: the gateway's own error code
    /// when it answered with one, otherwise whether the status is an error.
    /// </summary>
    private static string ResolveOutcome(HttpContext context, int statusCode)
    {
        if (context.Response.Headers.TryGetValue(GatewayHeaders.ErrorCode, out var code) &&
            code.ToString() is { Length: > 0 } errorCode)
        {
            return errorCode;
        }

        return statusCode >= StatusCodes.Status400BadRequest ? "error" : "success";
    }

    [LoggerMessage(
        EventId = 3300,
        EventName = "InferenceRequest",
        Level = LogLevel.Information,
        Message = "inference_request request_id={RequestId} tenant=\"{Tenant}\" key=\"{Key}\" model=\"{Model}\" "
            + "stream={Stream} status={StatusCode} outcome={Outcome} limit_decision={LimitDecision} "
            + "limit_scope={LimitScope} upstream_status={UpstreamStatus} duration_ms={DurationMs}")]
    private static partial void LogRequest(
        ILogger logger,
        string requestId,
        string tenant,
        string key,
        string model,
        bool stream,
        int statusCode,
        string outcome,
        string limitDecision,
        string limitScope,
        string upstreamStatus,
        long durationMs);
}
