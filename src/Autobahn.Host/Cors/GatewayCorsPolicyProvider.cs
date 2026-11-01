using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Hosting;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;

namespace Autobahn.Host.Cors;

/// <summary>
/// Builds the default CORS policy from the live config snapshot (<see cref="IGatewayConfigProvider"/>)
/// so admin updates take effect in-process without a restart.
/// </summary>
public sealed class GatewayCorsPolicyProvider(
    IGatewayConfigProvider configProvider,
    IHostEnvironment environment) : ICorsPolicyProvider
{
    public const int PreflightMaxAgeSeconds = 86_400;

    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        _ = policyName;
        return Task.FromResult<CorsPolicy?>(BuildPolicy());
    }

    private CorsPolicy BuildPolicy()
    {
        var builder = new CorsPolicyBuilder();

        if (environment.IsDevelopment())
        {
            builder.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
            return builder.Build();
        }

        var allowedOrigins = configProvider.Current.Cors.AllowedOrigins;
        if (allowedOrigins.Count > 0)
        {
            builder.SetIsOriginAllowed(origin => CorsOriginMatcher.IsOriginAllowed(origin, allowedOrigins))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .SetPreflightMaxAge(TimeSpan.FromSeconds(PreflightMaxAgeSeconds));
            return builder.Build();
        }

        builder.SetIsOriginAllowed(_ => false);
        return builder.Build();
    }
}
