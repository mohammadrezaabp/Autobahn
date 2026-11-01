using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;
using Autobahn.Application.Security;
using Autobahn.Persistence.DependencyInjection;
using Autobahn.Security.Authentication;
using Autobahn.Security.Authorization;
using Autobahn.Security.Configuration;
using Autobahn.Security.Hosting;
using Autobahn.Security.Audit;
using Autobahn.Security.Services;

namespace Autobahn.Security.DependencyInjection;
public sealed class GatewaySecurityOptionsValidator : IValidateOptions<GatewaySecurityOptions>
{
    private readonly IHostEnvironment _environment;
    public GatewaySecurityOptionsValidator(IHostEnvironment environment) => _environment = environment;
    public ValidateOptionsResult Validate(string? name, GatewaySecurityOptions options)
    {
        // The cache TTL is the revocation SLA: invalidation on write is in-process only, so on a
        // multi-replica deployment a revoked key stays usable on the other replicas until their
        // entry expires. Enforced in every environment — an unbounded TTL is a security problem
        // whether or not the deployment is production.
        if (options.CacheTtlMinutes < 1)
        {
            return ValidateOptionsResult.Fail($"{GatewaySecurityOptions.SectionName}:CacheTtlMinutes must be at least 1 minute.");
        }

        if (options.CacheTtlMinutes > GatewaySecurityOptions.MaximumCacheTtlMinutes)
        {
            return ValidateOptionsResult.Fail($"{GatewaySecurityOptions.SectionName}:CacheTtlMinutes must not exceed " + $"{GatewaySecurityOptions.MaximumCacheTtlMinutes} minutes. It bounds how long a revoked API key " + "or a removed model grant keeps working on replicas other than the one that processed the " + "revocation, because cache invalidation is in-process only.");
        }

        // The key pepper is only a development convenience default. Outside Development it protects
        // every stored API-key hash, so refuse to start with an empty, default, or too-short value
        // rather than silently hashing keys with a publicly-known secret.
        if (_environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        var pepper = options.KeyPepper?.Trim();
        if (string.IsNullOrEmpty(pepper) || Autobahn.Application.Security.WellKnownWeakSecrets.IsWeakPepper(pepper) || pepper.Length < GatewaySecurityOptions.MinimumPepperLength)
        {
            return ValidateOptionsResult.Fail($"{GatewaySecurityOptions.SectionName}:KeyPepper must be set to a strong, non-default " + $"value of at least {GatewaySecurityOptions.MinimumPepperLength} characters outside Development. " + "Set the GATEWAY_KEY_PEPPER environment variable to a freshly generated secret.");
        }

        return ValidateOptionsResult.Success;
    }
}
