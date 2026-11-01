using Microsoft.Extensions.Options;

namespace Autobahn.Host.Metrics;
/// <summary>
/// Tells the operator at boot how <c>/metrics</c> is gated, so a scraper that suddenly gets 401s
/// after an upgrade has an explanation in the log rather than a silent hole in the dashboards.
/// </summary>
internal sealed class GatewayMetricsStartupWarningHostedService(IOptions<GatewayMetricsOptions> options, ILogger<GatewayMetricsStartupWarningHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.AllowAnonymous)
        {
            logger.LogWarning("/metrics is served anonymously (Gateway:Metrics:AllowAnonymous=true). The exposition " + "carries per-model request, error, latency and token series; make sure the port is " + "reachable only from your scraper.");
        }
        else if (!settings.HasScrapeToken)
        {
            logger.LogWarning("/metrics requires an Operator API key: no scrape token is configured. To let a scraper " + "authenticate without a gateway key, set Gateway:Metrics:ScrapeToken (environment: " + "Gateway__Metrics__ScrapeToken, or GATEWAY_METRICS_SCRAPE_TOKEN in the compose stack) " + "and send it as 'Authorization: Bearer <token>'. To serve the scrape without any " + "credential set Gateway:Metrics:AllowAnonymous=true.");
        }
        else
        {
            logger.LogInformation("/metrics accepts the configured scrape token (Authorization: Bearer) or an Operator API key.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
