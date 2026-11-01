using Microsoft.AspNetCore.Http;
using Autobahn.Domain.Models;

namespace Autobahn.Proxy.Routing;

/// <summary>
/// How the router tells the limiter what a request really cost in tokens, so a token reservation
/// made on admission can be settled when the response ends.
/// </summary>
/// <remarks>
/// The limiter reserves before the router runs and settles after it returns, and the usage is only
/// known inside the router. Three outcomes have to stay apart: a measured cost replaces the
/// reservation; a request that was never served hands the whole reservation back; and a response
/// that was served but could not be measured keeps the reservation as its charge, because the
/// backend spent tokens the gateway could not read.
/// </remarks>
public static class RateLimitTokenUsage
{
    private static readonly object ActualKey = new();
    private static readonly object ServedKey = new();

    /// <param name="usage">The usage captured from the response, measured or estimated; null when none was.</param>
    /// <param name="served">Whether the upstream answered the request with a success status.</param>
    public static void Report(HttpContext context, UsageEvent? usage, bool served)
    {
        if (usage is not null)
        {
            context.Items[ActualKey] = usage.TokenSource == UsageTokenSource.TotalOnly
                ? usage.TotalTokens
                : usage.PromptTokens + usage.CompletionTokens;
        }

        if (served)
        {
            context.Items[ServedKey] = true;
        }
    }

    public static bool TryGetActual(HttpContext context, out long tokens)
    {
        if (context.Items.TryGetValue(ActualKey, out var value) && value is long actual)
        {
            tokens = actual;
            return true;
        }

        tokens = 0;
        return false;
    }

    public static bool WasServed(HttpContext context) => context.Items.ContainsKey(ServedKey);
}
