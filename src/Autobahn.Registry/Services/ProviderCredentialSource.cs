using Microsoft.Extensions.Configuration;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;

namespace Autobahn.Registry.Services;

public sealed class ProviderCredentialSource(
    IUpstreamSecretStore secretStore,
    IConfiguration configuration) : IProviderCredentialSource
{
    public string? Resolve(string provider)
    {
        if (!ModelProviders.TryGetPaid(provider, out var definition))
        {
            return null;
        }

        if (secretStore.TryGet(ProviderSecretKeys.StoreKey(definition.Id), out var stored) &&
            !string.IsNullOrWhiteSpace(stored))
        {
            return stored;
        }

        var fromEnvironment = configuration[definition.DefaultEnvVar]
            ?? Environment.GetEnvironmentVariable(definition.DefaultEnvVar);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
    }

    public ProviderCredentialOrigin OriginOf(string provider)
    {
        if (!ModelProviders.TryGetPaid(provider, out var definition))
        {
            return ProviderCredentialOrigin.None;
        }

        if (secretStore.TryGet(ProviderSecretKeys.StoreKey(definition.Id), out var stored) &&
            !string.IsNullOrWhiteSpace(stored))
        {
            return ProviderCredentialOrigin.Stored;
        }

        var fromEnvironment = configuration[definition.DefaultEnvVar]
            ?? Environment.GetEnvironmentVariable(definition.DefaultEnvVar);
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? ProviderCredentialOrigin.None
            : ProviderCredentialOrigin.Environment;
    }

    public Task StoreAsync(string provider, string apiKey, CancellationToken cancellationToken = default)
    {
        if (!ModelProviders.TryGetPaid(provider, out var definition))
        {
            throw new ArgumentException($"'{provider}' is not a paid provider.", nameof(provider));
        }

        return secretStore.PutAsync(ProviderSecretKeys.StoreKey(definition.Id), apiKey, cancellationToken);
    }

    public Task ClearStoredAsync(string provider, CancellationToken cancellationToken = default) =>
        ModelProviders.TryGetPaid(provider, out var definition)
            ? secretStore.DeleteAsync(ProviderSecretKeys.StoreKey(definition.Id), cancellationToken)
            : Task.CompletedTask;
}
