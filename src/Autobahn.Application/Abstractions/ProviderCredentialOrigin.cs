using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;
/// <summary>Where a paid provider's company key comes from.</summary>
public enum ProviderCredentialOrigin
{
    /// <summary>No key is configured; the provider cannot be called.</summary>
    None,
    /// <summary>The environment variable the provider is known by (<c>OPENROUTER_API_KEY</c>, …).</summary>
    Environment,
    /// <summary>A key an operator stored from the Providers page; it takes precedence.</summary>
    Stored,
}
