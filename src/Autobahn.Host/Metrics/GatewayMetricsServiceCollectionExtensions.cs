using Microsoft.Extensions.Options;

namespace Autobahn.Host.Metrics;
public static class GatewayMetricsServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayMetricsAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GatewayMetricsOptions>().Bind(configuration.GetSection(GatewayMetricsOptions.SectionName));
        services.AddHostedService<GatewayMetricsStartupWarningHostedService>();
        return services;
    }

    /// <summary>Gates <c>/metrics</c>; register after authentication and before the endpoints.</summary>
    public static IApplicationBuilder UseMetricsScrapeAuthorization(this IApplicationBuilder app) => app.UseMiddleware<MetricsScrapeAuthorizationMiddleware>();
}
