using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Application.Providers;
using Autobahn.Registry.Health;
using Autobahn.Registry.Hosting;
using Autobahn.Registry.Services;

namespace Autobahn.Registry.DependencyInjection;

public static class RegistryServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayRegistry(this IServiceCollection services)
    {
        services.AddSingleton<RegistryGate>();
        services.AddSingleton<ModelRegistryService>();
        services.AddSingleton<IModelRegistry>(sp => sp.GetRequiredService<ModelRegistryService>());
        services.AddSingleton<FileUpstreamSecretStore>();
        services.AddSingleton<IUpstreamSecretStore>(sp => sp.GetRequiredService<FileUpstreamSecretStore>());
        // The resolver enforces this policy at the point of use, so it must be present whenever the
        // registry is — not only when the admin API module happens to also be composed.
        services.TryAddSingleton(sp => UpstreamEnvVarPolicy.FromOptions(
            sp.GetRequiredService<IOptions<GatewayOptions>>().Value));
        services.AddSingleton<IProviderCredentialSource, ProviderCredentialSource>();
        services.AddSingleton<UpstreamBearerTokenResolver>();
        services.AddSingleton<IUpstreamBearerTokenResolver>(sp => sp.GetRequiredService<UpstreamBearerTokenResolver>());
        services.AddSingleton<ModelRegistryWriter>();
        services.AddSingleton<IModelRegistryWriter>(sp => sp.GetRequiredService<ModelRegistryWriter>());
        services.AddSingleton<BackendHealthStore>();
        services.AddSingleton<IBackendHealthStore>(sp => sp.GetRequiredService<BackendHealthStore>());
        services.AddHostedService<HealthCheckService>();
        services.AddSingleton<ModelRegistryLoader>();
        services.AddHostedService<ModelRegistryLoaderHostedService>();
        // After the loader: verification judges only the credentials a loaded model points at.
        services.AddHostedService<UpstreamSecretVerificationHostedService>();
        services.AddHostedService<ModelRegistryRouteReconcileService>();
        services.AddSingleton<IConfigReload, ModelRegistryConfigReload>();
        return services;
    }
}
