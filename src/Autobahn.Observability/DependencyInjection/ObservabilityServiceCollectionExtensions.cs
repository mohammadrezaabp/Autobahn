using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Observability.Attention;
using Autobahn.Observability.ControlPlane;
using Autobahn.Observability.Diagnostics;
using Autobahn.Observability.Metrics;
using Autobahn.Observability.Policy;
using Autobahn.Observability.RateLimiting;
using Autobahn.Observability.RecentRequests;
using Autobahn.Observability.Runtime;
using Autobahn.Observability.Summary;
using Autobahn.Observability.Tracking;
using Autobahn.Observability.Usage;

namespace Autobahn.Observability.DependencyInjection;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayObservability(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp =>
        {
            var options = sp.GetService<IOptions<GatewayOptions>>()?.Value.Overview.WindowedStats ?? new OverviewWindowedStatsOptions();
            return new RollingWindowStats(sp.GetRequiredService<TimeProvider>())
            {
                Enabled = options.Enabled,
                MaxTrackedModels = Math.Max(0, options.MaxTrackedModels),
            };
        });
        services.AddSingleton(sp => new GatewayRuntimeState(sp.GetRequiredService<RollingWindowStats>()));
        services.AddHostedService<GatewayOverviewSamplerHostedService>();
        services.AddSingleton(sp => new PolicyPressureTracker(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<RateLimitUsageTracker>();
        services.AddSingleton<IRateLimitUsageTracker>(sp => sp.GetRequiredService<RateLimitUsageTracker>());
        services.AddSingleton<MetricCallerBudget>();
        services.AddSingleton<CallerOpenStreams>();
        services.AddSingleton<GatewayMetricsCollector>();
        services.AddSingleton<IGatewayMetricsCollector>(sp => sp.GetRequiredService<GatewayMetricsCollector>());
        services.AddSingleton<IUsageQualityCounters>(sp => sp.GetRequiredService<GatewayMetricsCollector>());
        services.AddSingleton<IRequestTracker, GatewayRequestTracker>();
        services.AddSingleton<IRecentRequestStore, InMemoryRecentRequestStore>();
        services.AddSingleton<IAdminLiveFeed, GatewayAdminLiveFeed>();
        services.AddSingleton<IGatewayLogStore, InMemoryGatewayLogStore>();

        // The in-memory store is the floor: it serves the Errors tab on its own when no database is
        // configured, and the composition root swaps in the database-backed facade when one is.
        // Plain AddSingleton, not TryAdd: a later registration of the facade must win.
        services.AddSingleton<InMemoryGatewayErrorStore>();
        services.AddSingleton<IGatewayErrorStore>(sp => sp.GetRequiredService<InMemoryGatewayErrorStore>());
        services.AddSingleton<IGatewayErrorRecorder>(sp => sp.GetRequiredService<InMemoryGatewayErrorStore>());
        // Registering the sink as an ILoggerProvider in the container makes every ILogger warning
        // and error in the process visible in the admin Logs tab, with no call-site changes.
        // This only takes effect because the host opts into writeToProviders — see
        // GatewayHostBuilderExtensions.ConfigureGatewayHost.
        services.AddSingleton<ILoggerProvider>(sp =>
            new GatewayLogSinkProvider(
                sp.GetRequiredService<IGatewayLogStore>,
                sp.GetRequiredService<IGatewayErrorRecorder>,
                sp.GetRequiredService<IOptions<GatewayErrorTrackingOptions>>().Value));
        services.AddSingleton<IAttentionEvaluator, AttentionEvaluator>();
        services.AddSingleton<IAdminSummaryReader, GatewayAdminSummaryReader>();
        services.AddSingleton<IControlPlaneCommands, ControlPlaneCommands>();
        services.AddSingleton<ChannelUsageRecorder>();
        services.AddSingleton<IUsageRecorder>(sp => sp.GetRequiredService<ChannelUsageRecorder>());
        services.AddSingleton<IUsageWriterStateSource>(sp => sp.GetRequiredService<ChannelUsageRecorder>());
        services.AddHostedService(sp => sp.GetRequiredService<ChannelUsageRecorder>());
        services.AddHostedService<GatewayBackendHealthMetricsExporter>();
        services.AddHostedService<GatewayFinOpsMetricsExporter>();
        services.TryAddSingleton<Autobahn.Application.Providers.ProviderStatusStore>();
        services.AddHostedService<GatewayProviderMetricsExporter>();
        services.AddHostedService<GatewayCircuitBreakerMetricsExporter>();
        services.AddHostedService<GatewayRateLimitMetricsExporter>();
        services.AddHostedService<GatewayCallerStreamsMetricsExporter>();
        services.AddHostedService<GatewayProcessInfoMetricsExporter>();

        return services;
    }
}
