using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Autobahn.Persistence.Bootstrap;

namespace Autobahn.Persistence.Hosting;

public sealed class GatewayDbInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public GatewayDbInitializer(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<GatewayDbBootstrap>();
        await bootstrap.EnsureInitializedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
