using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;
/// <summary>
/// The one company key the gateway holds for each paid provider.
/// </summary>
/// <remarks>
/// The key belongs to the provider, not to a model: every backend at that provider that names the
/// provider's environment variable is sent the same key, and replacing it replaces it for all of
/// them at once.
/// </remarks>
public interface IProviderCredentialSource
{
    string? Resolve(string provider);
    ProviderCredentialOrigin OriginOf(string provider);
    /// <summary>Stores a key that takes precedence over the environment variable.</summary>
    Task StoreAsync(string provider, string apiKey, CancellationToken cancellationToken = default);
    /// <summary>Removes the stored key, so the environment variable is used again.</summary>
    Task ClearStoredAsync(string provider, CancellationToken cancellationToken = default);
}
