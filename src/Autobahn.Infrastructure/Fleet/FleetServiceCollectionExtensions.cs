using Autobahn.Application.Fleet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autobahn.Infrastructure.Fleet;

public static class FleetServiceCollectionExtensions
{
    public static IServiceCollection AddFleet(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FleetOptions>().Bind(configuration.GetSection("Fleet"))
            .Validate(x => x.PollSeconds is >= 2 and <= 300 && x.SessionHours is >= 1 and <= 24, "Fleet polling or session lifetime is invalid.")
            .Validate(x => x.Nodes.Select(n => n.Id).Distinct().Count() == x.Nodes.Length && x.Nodes.All(n =>
                System.Text.RegularExpressions.Regex.IsMatch(n.Id, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,31}$") &&
                Uri.TryCreate(n.Address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
                string.IsNullOrEmpty(uri.UserInfo) && n.Token.Length >= 32), "Fleet nodes require unique IDs, HTTP addresses and tokens of at least 32 characters.")
            .ValidateOnStart();
        services.AddSingleton<IFleetStateStore>(sp => new SqliteFleetStateStore(sp.GetRequiredService<IOptions<FleetOptions>>().Value.DataDirectory));
        services.AddHttpClient("fleet-agents", c => { c.Timeout = TimeSpan.FromMinutes(65); c.MaxResponseContentBufferSize = 16 * 1024 * 1024; })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddHttpClient("fleet-gitops", c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddHttpClient("fleet-catalog", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IFleetAgentClient, FleetAgentClient>();
        services.AddSingleton<IGitOpsClient, GitLabGitOpsClient>();
        services.AddSingleton<FleetPoller>();
        services.AddSingleton<FleetMetrics>();
        services.AddHostedService(sp => sp.GetRequiredService<FleetPoller>());
        services.AddSingleton<FleetControlService>();
        return services;
    }
}
