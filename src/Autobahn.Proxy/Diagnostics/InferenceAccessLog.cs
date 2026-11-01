using Microsoft.AspNetCore.Http;
using Autobahn.Domain.RateLimiting;

namespace Autobahn.Proxy.Diagnostics;

/// <summary>
/// What the request path leaves behind for the one log line written per inference request.
/// </summary>
/// <remarks>
/// The rate limiter and the router each know part of what happened to a request, and neither sees
/// all of it: a request the limiter refuses never reaches the router. They note what they decided
/// on the <see cref="HttpContext"/>, and <see cref="Middleware.InferenceAccessLogMiddleware"/> writes
/// the line once the response is finished.
/// </remarks>
public static class InferenceAccessLog
{
    public const string Unknown = "unknown";

    public const string None = "none";

    internal const string ModelKey = "GatewayAccessLogModel";

    internal const string OutcomeKey = "GatewayAccessLogOutcome";

    internal const string UpstreamStatusKey = "GatewayAccessLogUpstreamStatus";

    internal const string LimitDecisionKey = "GatewayAccessLogLimitDecision";

    internal const string LimitScopeKey = "GatewayAccessLogLimitScope";

    /// <summary>The canonical model id, once the registry has resolved it.</summary>
    public static void SetModel(HttpContext context, string modelId) =>
        context.Items[ModelKey] = modelId;

    /// <summary>How the request ended, in the words <c>gateway_forward_attempts_total</c> uses.</summary>
    public static void SetOutcome(HttpContext context, string outcome) =>
        context.Items[OutcomeKey] = outcome;

    /// <summary>The status the upstream answered with. Not set when it never answered.</summary>
    public static void SetUpstreamStatus(HttpContext context, int statusCode) =>
        context.Items[UpstreamStatusKey] = statusCode;

    /// <summary>
    /// The rate limiter's decision. A later refusal replaces an earlier admission, so a streaming
    /// request the rate stage admitted and a stream cap then refused reads as refused.
    /// </summary>
    /// <param name="scope">The scope that refused; ignored on an admission.</param>
    public static void SetLimitDecision(HttpContext context, bool admitted, RateLimitScope? scope)
    {
        context.Items[LimitDecisionKey] = admitted ? "admitted" : "refused";
        context.Items[LimitScopeKey] = admitted ? None : scope?.ToLabel() ?? RateLimitScopeNames.Tenant;
    }

    internal static string Read(HttpContext context, string key, string fallback) =>
        context.Items.TryGetValue(key, out var value) && value is string text && text.Length > 0
            ? text
            : fallback;
}
