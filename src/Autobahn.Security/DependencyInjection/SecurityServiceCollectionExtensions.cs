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
public static class SecurityServiceCollectionExtensions
{
    /// <param name = "environment">
    /// The host environment, used to decide whether running without a database — and therefore
    /// without authentication — is acceptable. Optional so existing callers keep compiling; when it
    /// is not supplied the environment is read from configuration and, failing that, assumed to be
    /// Production. Assuming Production is the point: an unknown environment must not be the reason a
    /// gateway starts with its control plane open.
    /// </param>
    public static IServiceCollection AddGatewaySecurity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment? environment = null)
    {
        var connectionString = configuration.GetConnectionString(PersistenceServiceCollectionExtensions.ConnectionStringName);
        services.AddSingleton<GatewayAuthenticationState>();
        services.AddSingleton<IGatewayAuthenticationState>(sp => sp.GetRequiredService<GatewayAuthenticationState>());
        // Resolution order documented on OperatorTenantConfiguration: explicit security setting,
        // else the bootstrap tenant slug, else "default". Read as raw keys because the bootstrap
        // section belongs to Persistence and this module must not depend on its options type.
        var operatorTenantSlug = configuration[$"{GatewaySecurityOptions.SectionName}:OperatorTenantSlug"];
        if (string.IsNullOrWhiteSpace(operatorTenantSlug))
        {
            operatorTenantSlug = configuration["Gateway:Bootstrap:TenantSlug"];
        }

        services.AddSingleton(new OperatorTenantConfiguration(string.IsNullOrWhiteSpace(operatorTenantSlug) ? OperatorTenantConfiguration.FallbackTenantSlug : operatorTenantSlug.Trim()));
        services.AddSingleton<IAuthorizationHandler, GatewayAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(GatewayAuthPolicies.Inference, policy => policy.AddRequirements(new GatewayAuthorizationRequirement(GatewayAuthPolicies.Inference)));
            options.AddPolicy(GatewayAuthPolicies.Admin, policy => policy.AddRequirements(new GatewayAuthorizationRequirement(GatewayAuthPolicies.Admin)));
            options.AddPolicy(GatewayAuthPolicies.Operator, policy => policy.AddRequirements(new GatewayAuthorizationRequirement(GatewayAuthPolicies.Operator)));
        });
        services.AddAuthentication(GatewayAuthSchemes.ApiKey).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(GatewayAuthSchemes.ApiKey, _ =>
        {
        });
        // Bound before the anonymous-mode return below: the audit trail reads its path and size cap
        // from this section, and a gateway running without a database still exposes the control plane
        // and so still has admin actions worth recording.
        services.AddOptions<GatewaySecurityOptions>().Bind(configuration.GetSection(GatewaySecurityOptions.SectionName));
        // A durable trail, not just a log line. Every admin mutation — key create/revoke, model
        // grants, CORS, rate limits, config reload, database backup — used to be recorded only as an
        // ILogger Information event, so whether it survived depended entirely on the deployed Serilog
        // configuration (console sink only, by default) and it could not be reviewed from the console.
        services.AddSingleton<FileAuditLogger>();
        services.AddSingleton<IAuditLogger>(sp => sp.GetRequiredService<FileAuditLogger>());
        services.AddSingleton<IAuditLogReader, FileAuditLogReader>();
        // Registered for both branches. The key pepper encrypts the upstream provider secrets file,
        // which a gateway without a database still reads and writes, so "no database" is not a
        // reason to stop checking it — and CacheTtlMinutes is validated in every environment.
        services.AddSingleton<IValidateOptions<GatewaySecurityOptions>, GatewaySecurityOptionsValidator>();
        services.AddOptions<GatewaySecurityOptions>().ValidateOnStart();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Decided here, at the point the missing connection string is detected, and eagerly.
            // This used to live in GatewayAuthenticationInitializer.StartAsync — which is registered
            // below, inside the branch this one returns before reaching, so the guard could never
            // run in the one configuration it was written for: a Production deploy shipping the
            // default (empty) connection string started with IsAuthenticationRequired left at its
            // `false` default and served the whole control plane anonymously, without even the
            // warning. Throwing from registration also means the decision lands before Kestrel
            // binds a port, so there is no window in which an unauthenticated gateway is listening.
            GuardAnonymousFallback(configuration, ResolveIsDevelopment(configuration, environment));
            services.AddSingleton<IApiKeyValidator, NullApiKeyValidator>();
            services.AddSingleton<IModelGrantService, NullModelGrantService>();
            services.AddSingleton<IModelGrantAdminService, NullModelGrantAdminService>();
            services.AddSingleton<IProviderAccessAdminService, NullProviderAccessAdminService>();
            services.AddSingleton<IAdminKeyService, NullAdminKeyService>();
            services.AddSingleton<ITenantAdminService, NullTenantAdminService>();
            // Still registered without a database: it is what sets IsAuthenticationRequired
            // explicitly rather than leaving it at a default, and what logs the warning saying the
            // gateway is running open. Its own copy of the guard above is kept as defence in depth.
            services.AddHostedService<GatewayAuthenticationInitializer>();
            return services;
        }

        services.AddMemoryCache();
        services.AddSingleton<ApiKeyNegativeCache>();
        services.AddScoped<IApiKeyValidator, ApiKeyValidator>();
        // Singleton: answers from cache and only opens a scope (and a DbContext) on a miss.
        services.AddSingleton<IModelGrantService, ModelGrantService>();
        services.AddScoped<IModelGrantAdminService, ModelGrantAdminService>();
        services.AddScoped<IProviderAccessAdminService, ProviderAccessAdminService>();
        services.AddScoped<IAdminKeyService, AdminKeyService>();
        services.AddScoped<ITenantAdminService, TenantAdminService>();
        services.AddScoped<IApiKeyLastUsedTracker, DebouncedApiKeyLastUsedTracker>();
        services.AddHostedService<GatewayAuthenticationInitializer>();
        return services;
    }

    public static IApplicationBuilder UseGatewaySecurity(this IApplicationBuilder app, IConfiguration configuration)
    {
        // Required for endpoint RequireAuthorization in every configuration.
        app.UseAuthentication();
        app.UseAuthorization();
        // Unconditional. Registering it only when a database was configured left the control plane
        // of a DB-less host with no path-based authorization at all, which is precisely the host
        // that has no key store to fall back on. The middleware lets anonymous paths and anonymous
        // inference through on their own merits, so there is nothing for the connection string to
        // decide here.
        app.UseMiddleware<Middleware.GatewayAuthorizationMiddleware>();
        return app;
    }

    /// <summary>
    /// Refuses to configure a gateway that would run without authentication unless that is plainly
    /// what the operator meant.
    /// </summary>
    /// <remarks>
    /// Without a database there is no key store, so every request is anonymous and every endpoint —
    /// the admin control plane included — is open. That is a reasonable local-development default
    /// and a legitimate deliberate choice; it is never an acceptable accident. Two ways to say you
    /// meant it: run in Development, or set
    /// <c>Gateway:Security:AllowAnonymous=true</c>.
    /// </remarks>
    /// <exception cref = "InvalidOperationException">
    /// The gateway has no connection string, is not in Development, and has not opted in.
    /// </exception>
    internal static void GuardAnonymousFallback(IConfiguration configuration, bool isDevelopment)
    {
        if (isDevelopment)
        {
            return;
        }

        var allowAnonymous = bool.TryParse(configuration[$"{GatewaySecurityOptions.SectionName}:AllowAnonymous"], out var anonymousOptIn) && anonymousOptIn;
        if (allowAnonymous)
        {
            return;
        }

        throw new InvalidOperationException("Gateway requires a configured database connection string " + $"('ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}') " + "outside Development: without one there is no API key store, so authentication is " + "disabled and every endpoint — including the admin control plane — would be reachable " + "anonymously. To intentionally run without authentication, set " + $"'{GatewaySecurityOptions.SectionName}:AllowAnonymous=true'.");
    }

    /// <summary>
    /// The host environment at registration time, falling back to configuration and finally to
    /// Production — matching <see cref = "IHostEnvironment"/>, which also treats an unset
    /// environment name as Production.
    /// </summary>
    private static bool ResolveIsDevelopment(IConfiguration configuration, IHostEnvironment? environment)
    {
        if (environment is not null)
        {
            return environment.IsDevelopment();
        }

        var name = configuration[HostDefaults.EnvironmentKey] ?? configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        return string.Equals(name, Environments.Development, StringComparison.OrdinalIgnoreCase);
    }
}
