using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;
public static class ProviderSecretKeys
{
    private const string Prefix = "@provider:";
    /// <summary>The key a provider's stored credential is filed under in the secret store.</summary>
    public static string StoreKey(string provider) => Prefix + provider.Trim().ToLowerInvariant();
    public static bool IsProviderKey(string storeKey) => storeKey.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    /// <summary>
    /// The paid provider whose company key <paramref name = "upstreamAuth"/> asks for: the one whose
    /// environment variable it names. Null for a credential of the model's own.
    /// </summary>
    public static string? ProviderNamedBy(UpstreamAuthConfig? upstreamAuth)
    {
        var envVar = upstreamAuth?.EnvVar?.Trim();
        if (string.IsNullOrEmpty(envVar))
        {
            return null;
        }

        foreach (var definition in ModelProviders.Paid)
        {
            if (string.Equals(definition.DefaultEnvVar, envVar, StringComparison.OrdinalIgnoreCase))
            {
                return definition.Id;
            }
        }

        return null;
    }
}
