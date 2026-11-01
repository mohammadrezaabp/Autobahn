using Microsoft.Extensions.DependencyInjection;
using Autobahn.Application.Abstractions;

namespace Autobahn.Host.DependencyInjection.Overview;

public static class GatewayOverviewServiceCollectionExtensions
{
    /// <summary>Registers the Overview's composition-root sections: one singleton seen through three Core interfaces.</summary>
    public static IServiceCollection AddGatewayOverview(this IServiceCollection services)
    {
        services.AddSingleton<GatewayOverviewSectionService>();
        services.AddSingleton<IOverviewSectionService>(sp => sp.GetRequiredService<GatewayOverviewSectionService>());
        services.AddSingleton<IOverviewSlowSectionCache>(sp => sp.GetRequiredService<GatewayOverviewSectionService>());
        services.AddSingleton<IOverviewHotSectionSource>(sp => sp.GetRequiredService<GatewayOverviewSectionService>());
        services.AddHostedService<GatewayOverviewRefreshHostedService>();
        return services;
    }
}
