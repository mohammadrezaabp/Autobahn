using System.Diagnostics;
using Autobahn.Application.Observability;

namespace Autobahn.Observability.Metrics;

internal static class GatewayTokenMetricsRecorder
{
    /// <param name="caller">Already resolved through <see cref="MetricCallerBudget"/>.</param>
    public static void Record(
        string modelId,
        long promptTokens,
        long completionTokens,
        MetricCaller caller,
        string provider = "self-hosted")
    {
        if (promptTokens > 0)
        {
            GatewayMeters.TokensTotal.Add(promptTokens, Tags(modelId, "input", caller, provider));
        }

        if (completionTokens > 0)
        {
            GatewayMeters.TokensTotal.Add(completionTokens, Tags(modelId, "output", caller, provider));
        }

        var total = promptTokens + completionTokens;
        if (total > 0)
        {
            GatewayMeters.TokensTotal.Add(total, Tags(modelId, "total", caller, provider));
        }
    }

    private static TagList Tags(string modelId, string direction, MetricCaller caller, string provider) =>
        new()
        {
            { "model", modelId },
            { "direction", direction },
            { "tenant", caller.Tenant },
            { "key", caller.Key },
            { "provider", provider },
        };
}
