using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Hosting;
using Autobahn.Host.Cors;

namespace Autobahn.Host.DependencyInjection;

public static class GatewayCorsServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _ = configuration;
        _ = environment;

        services.AddCors();
        services.AddSingleton<ICorsPolicyProvider, GatewayCorsPolicyProvider>();
        services.AddHostedService<GatewayCorsStartupWarningHostedService>();

        return services;
    }
}
