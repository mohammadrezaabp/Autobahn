using Microsoft.Extensions.DependencyInjection;
using Autobahn.Domain.Configuration;
using Autobahn.Persistence.DependencyInjection;

namespace Autobahn.Host.DependencyInjection;

public static class GatewayStatsPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Wires the background services that persist and restore the dashboard counters, monthly
    /// quota usage and the spent rate-limit buckets. No-op unless a database connection string is configured (mirrors
    /// <c>AddGatewayBillingPersistence</c>). Must be registered after <c>AddGatewayPersistence</c>
    /// so database migrations run before this service's startup restore.
    /// </summary>
    public static IServiceCollection AddGatewayStatsPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(
            PersistenceServiceCollectionExtensions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        services
            .AddOptions<GatewayStatsPersistenceOptions>()
            .Bind(configuration.GetSection(GatewayStatsPersistenceOptions.SectionName));

        services.AddHostedService<GatewayStatsSnapshotService>();
        services.AddHostedService<RateLimitBucketSnapshotService>();

        return services;
    }
}
