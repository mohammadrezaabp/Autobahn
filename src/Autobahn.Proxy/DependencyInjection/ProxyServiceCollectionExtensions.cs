using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Proxy.Forwarding;
using Autobahn.Proxy.Hosting;
using Autobahn.Proxy.Middleware;
using Autobahn.Proxy.Resilience;
using Yarp.ReverseProxy.Forwarder;

namespace Autobahn.Proxy.DependencyInjection;

public static class ProxyServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayProxy(this IServiceCollection services)
    {
        services.AddHttpForwarder();
        services.AddSingleton<IInferenceHttpForwarder, InferenceHttpForwarder>();
        services.AddSingleton<IGatewayDrainState, GatewayDrainState>();
        services.AddSingleton<ModelCircuitBreakerRegistry>();
        services.AddSingleton<ICircuitBreakerStateSource>(sp =>
            sp.GetRequiredService<ModelCircuitBreakerRegistry>());
        services.AddSingleton<BulkheadRegistry>();
        services.AddSingleton<BackendInFlightTracker>();
        services.AddSingleton<IBackendInFlightSource>(sp => sp.GetRequiredService<BackendInFlightTracker>());
        services.AddSingleton<IBulkheadStateSource>(sp => sp.GetRequiredService<BulkheadRegistry>());
        services.AddHostedService<GatewayShutdownHostedService>();
        return services;
    }

    public static IApplicationBuilder UseGatewayExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayExceptionHandlingMiddleware>();

    /// <summary>
    /// Writes one line per inference request. Must be registered after the request id and before
    /// <c>UseGatewayExceptionHandling</c>, so the line carries the id and the status the handler
    /// settled on.
    /// </summary>
    public static IApplicationBuilder UseInferenceAccessLog(this IApplicationBuilder app) =>
        app.UseMiddleware<InferenceAccessLogMiddleware>();

    /// <summary>
    /// Refuses over-budget anonymous inference before the body is read. Must be registered
    /// immediately before <c>UsePublicModelDetection</c>, which is the first thing to buffer and
    /// parse it.
    /// </summary>
    public static IApplicationBuilder UseAnonymousAdmissionGuard(this IApplicationBuilder app) =>
        app.UseMiddleware<AnonymousAdmissionGuardMiddleware>();

    public static IApplicationBuilder UsePublicModelDetection(this IApplicationBuilder app) =>
        app.UseMiddleware<PublicModelDetectionMiddleware>();

    public static IApplicationBuilder UseInferenceResilience(this IApplicationBuilder app) =>
        app.UseMiddleware<InferenceResilienceMiddleware>();

    /// <summary>
    /// Wraps the security middleware so requests it refuses are still bounded. Must be registered
    /// immediately before <c>UseGatewaySecurity</c>.
    /// </summary>
    public static IApplicationBuilder UseAuthFailureRateLimiting(this IApplicationBuilder app) =>
        app.UseMiddleware<AuthFailureRateLimitMiddleware>();

    public static IApplicationBuilder UseGatewayRateLimiting(this IApplicationBuilder app) =>
        app.UseMiddleware<RateLimitMiddleware>();

    public static IApplicationBuilder UseGatewayQuotas(this IApplicationBuilder app) =>
        app.UseMiddleware<QuotaMiddleware>();

    public static IApplicationBuilder UseModelRouter(this IApplicationBuilder app) =>
        app.UseMiddleware<ModelRouterMiddleware>();

}
