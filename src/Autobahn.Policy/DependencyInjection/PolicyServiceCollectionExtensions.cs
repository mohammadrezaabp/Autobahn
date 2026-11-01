using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Policy.Admin;
using Autobahn.Policy.Quotas;
using Autobahn.Policy.RateLimiting;

namespace Autobahn.Policy.DependencyInjection;

public static class PolicyServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayPolicy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName));

        services
            .AddOptions<QuotaOptions>()
            .Bind(configuration.GetSection(QuotaOptions.SectionName));

        services.AddSingleton<IRateLimitPolicyResolver, RateLimitPolicyResolver>();
        services.AddSingleton<RateLimitPlanResolver>();
        services.AddSingleton<IRateLimitPlanResolver>(sp => sp.GetRequiredService<RateLimitPlanResolver>());
        services.AddSingleton<AdaptiveRateLimitGovernor>();
        services.AddSingleton<IAdaptiveRateLimitGovernor>(sp => sp.GetRequiredService<AdaptiveRateLimitGovernor>());
        services.AddHostedService<RateLimitMaintenanceHostedService>();
        services.AddSingleton<IRateLimitConfigAdminService, RateLimitConfigAdminService>();
        services.AddSingleton<ICorsConfigAdminService, CorsConfigAdminService>();
        services.AddSingleton<InMemoryDistributedRateLimitStore>();
        services.AddSingleton<IDistributedRateLimitStore>(sp => sp.GetRequiredService<InMemoryDistributedRateLimitStore>());
        services.AddSingleton<IRateLimitBucketSnapshotSource>(sp => sp.GetRequiredService<InMemoryDistributedRateLimitStore>());
        services.AddSingleton<InMemoryQuotaService>();
        services.AddSingleton<IQuotaService>(sp => sp.GetRequiredService<InMemoryQuotaService>());
        services.AddSingleton<IQuotaUsageSnapshotSource>(sp => sp.GetRequiredService<InMemoryQuotaService>());

        return services;
    }
}
