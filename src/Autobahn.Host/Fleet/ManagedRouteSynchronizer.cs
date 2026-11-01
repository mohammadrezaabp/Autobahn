using System.Text.Json.Nodes;
using Autobahn.Application.Abstractions;
using Autobahn.Application.Fleet;
using Autobahn.Domain.Fleet;
using Autobahn.Domain.Models;

namespace Autobahn.Host.Fleet;

public sealed class ManagedRouteSynchronizer(IServiceScopeFactory scopes, IFleetAgentClient agents) : IManagedRouteSynchronizer
{
    public async Task PublishAsync(string host, ServerSpecification server, CancellationToken cancellationToken)
    {
        var node = agents.Nodes.Single(x => x.Id == host);
        if (string.IsNullOrWhiteSpace(node.InferenceAddress)) throw new FleetException(409, "inference_address_required", "Configure this node's inference address before publishing its route.");
        var uri = new Uri(node.InferenceAddress);
        var address = new UriBuilder(uri) { Port = server.Port, Path = "", Query = "", Fragment = "" }.Uri.ToString().TrimEnd('/');
        using var scope = scopes.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IModelRegistry>();
        var writer = scope.ServiceProvider.GetRequiredService<IModelRegistryWriter>();
        var secrets = scope.ServiceProvider.GetRequiredService<IUpstreamSecretStore>();
        var name = host + "-" + server.Name;
        var key = await agents.CallAsync(host, HttpMethod.Get, $"/v1/servers/{Uri.EscapeDataString(server.Name)}/key", cancellationToken: cancellationToken);
        var token = key["key"]?.ToString() ?? "";
        if (server.ApiKey && token.Length == 0) throw new FleetException(502, "server_key_missing", "The node did not provide the model server credential.");
        if (token.Length > 0) await secrets.PutAsync(UpstreamSecretRefs.StoreKeyForBackend(server.ServedName, name), token, cancellationToken);
        var backend = new ModelBackendConfig { Name = name, Url = address, UpstreamModel = server.ServedName, Labels = new Dictionary<string, string> { ["managed-by"] = "autobahn", ["host"] = host, ["server"] = server.Name },
            UpstreamAuth = token.Length > 0 ? new UpstreamAuthConfig { SecretRef = UpstreamSecretRefs.ForBackend(server.ServedName, name) } : null };
        RegistryMutationResult result;
        if (!registry.TryGetModel(server.ServedName, out var existing)) result = await writer.AddModelAsync(new ModelConfig { Id = server.ServedName, Backends = [backend] }, cancellationToken);
        else if (ModelBackends.FindBackend(existing!, name) is not null) result = await writer.UpdateBackendAsync(server.ServedName, name, backend, cancellationToken);
        else result = await writer.AddBackendAsync(server.ServedName, backend, cancellationToken);
        if (!result.Success) throw new FleetException(result.SuggestedStatusCode, "route_sync_failed", result.Message);
    }

    public async Task RemoveAsync(string host, ServerSpecification server, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IModelRegistry>();
        if (!registry.TryGetModel(server.ServedName, out var model)) return;
        var name = host + "-" + server.Name;
        var backend = ModelBackends.FindBackend(model!, name);
        if (backend?.Labels.GetValueOrDefault("managed-by") != "autobahn") return;
        var writer = scope.ServiceProvider.GetRequiredService<IModelRegistryWriter>();
        var result = await writer.RemoveBackendAsync(server.ServedName, name, cancellationToken);
        if (!result.Success) throw new FleetException(result.SuggestedStatusCode, "route_sync_failed", result.Message);
        await scope.ServiceProvider.GetRequiredService<IUpstreamSecretStore>().DeleteAsync(UpstreamSecretRefs.StoreKeyForBackend(server.ServedName, name), cancellationToken);
    }
}
