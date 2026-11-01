using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Autobahn.Api.Security;
using Autobahn.Application.Abstractions;
using Autobahn.Application.Errors;
using Autobahn.Application.Security;
using Yarp.ReverseProxy.Forwarder;

namespace Autobahn.Host.Panels;

public static class GatewayPanelsEndpoints
{
    public static IServiceCollection AddGatewayPanels(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<GatewayPanelsOptions>()
            .Bind(configuration.GetSection(GatewayPanelsOptions.SectionName))
            .Validate(
                o => GatewayPanelsOptions.IsUsableUpstream(o.GrafanaUrl) &&
                     GatewayPanelsOptions.IsUsableUpstream(o.PrometheusUrl),
                "Gateway:Panels:GrafanaUrl and Gateway:Panels:PrometheusUrl must be absolute http(s) URLs, or empty to disable the panel.")
            .Validate(o => o.SessionMinutes > 0, "Gateway:Panels:SessionMinutes must be greater than zero.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<GatewayPanelCatalog>();
        services.AddSingleton<PanelSessionTokens>();
        services.AddSingleton<PanelUpstreamClient>();
        return services;
    }

    public static IEndpointRouteBuilder MapGatewayPanelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Operator, like /metrics: both panels show every tenant's traffic.
        var api = endpoints.MapGroup("/admin/api/panels")
            .RequireAuthorization(GatewayAuthPolicies.Operator);

        api.MapGet("/", ListAsync);
        api.MapPost("/session", OpenSession);
        api.MapDelete("/session", CloseSession);

        foreach (var prefix in GatewayPanelCatalog.PathPrefixes)
        {
            // The catch-all also matches the bare prefix, which ProxyAsync redirects.
            endpoints.Map(prefix + "/{**rest}", (RequestDelegate)ProxyAsync)
                .ExcludeFromDescription();
        }

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        GatewayPanelCatalog catalog,
        PanelUpstreamClient upstream,
        IOptions<GatewayPanelsOptions> options,
        CancellationToken cancellationToken)
    {
        var answers = await Task.WhenAll(catalog.Panels.Select(async panel => new PanelLink(
            panel.Id,
            panel.Label,
            panel.PathPrefix + "/",
            await upstream.IsReachableAsync(panel, cancellationToken).ConfigureAwait(false)))).ConfigureAwait(false);
        var panels = answers.ToList();

        // Logs is a page inside Grafana, not a panel of its own: it shares Grafana's route, session
        // cookie and health.
        var logsDashboard = options.Value.LogsDashboard?.Trim().Trim('/');
        var grafana = panels.Find(p => p.Id == GatewayPanelCatalog.GrafanaId);
        if (grafana is not null && !string.IsNullOrEmpty(logsDashboard))
        {
            panels.Add(new PanelLink("logs", "Logs", grafana.Path + logsDashboard, grafana.Reachable));
        }

        return Results.Json(new { panels });
    }

    private sealed record PanelLink(string Id, string Label, string Path, bool Reachable);

    private static IResult OpenSession(
        HttpContext httpContext,
        GatewayPanelCatalog catalog,
        PanelSessionTokens tokens,
        IOptions<GatewayPanelsOptions> options,
        IAuditLogger audit)
    {
        var lifetime = TimeSpan.FromMinutes(options.Value.SessionMinutes);
        var token = tokens.Issue(lifetime);
        foreach (var panel in catalog.Panels)
        {
            httpContext.Response.Cookies.Append(
                PanelSessionTokens.CookieName,
                token,
                SessionCookie(httpContext.Request, panel, lifetime));
        }

        audit.LogAdminAction(
            "panels.session.open",
            new AuditLogEntry(
                httpContext.User.FindFirst(GatewayAuthClaims.TenantId)?.Value,
                httpContext.User.FindFirst(GatewayAuthClaims.ApiKeyId)?.Value,
                new { Panels = catalog.Panels.Select(p => p.Id).ToArray() }));

        return Results.NoContent();
    }

    private static IResult CloseSession(HttpContext httpContext, GatewayPanelCatalog catalog)
    {
        foreach (var panel in catalog.Panels)
        {
            httpContext.Response.Cookies.Delete(
                PanelSessionTokens.CookieName,
                SessionCookie(httpContext.Request, panel, lifetime: null));
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Scoped to the panel's own path, so the browser never attaches it to <c>/admin/api</c>.
    /// </summary>
    private static CookieOptions SessionCookie(HttpRequest request, GatewayPanel panel, TimeSpan? lifetime) => new()
    {
        Path = panel.PathPrefix,
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        // The forwarded scheme is read directly: Gateway:ForwardedHeaders may be off behind a
        // TLS-terminating proxy, and trusting the header here can only make the cookie stricter.
        Secure = request.IsHttps ||
                 string.Equals(request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase),
        MaxAge = lifetime,
        IsEssential = true,
    };

    private static async Task ProxyAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var panel = services.GetRequiredService<GatewayPanelCatalog>().Find(context.Request.Path);
        if (panel is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var hasSession = services.GetRequiredService<PanelSessionTokens>()
            .IsValid(context.Request.Cookies[PanelSessionTokens.CookieName]);
        if (hasSession)
        {
            if (IsFromAnotherOrigin(context.Request))
            {
                // SameSite=Strict already keeps the cookie off cross-site requests. It does not
                // cover a sibling subdomain, which is the same site and a different origin.
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync(
                    $"Open {panel.Label} from the Autobahn admin console.",
                    context.RequestAborted).ConfigureAwait(false);
                return;
            }
        }
        else if (!await services.GetRequiredService<GatewayOperatorAccess>()
                     .IsOperatorAsync(context, context.RequestAborted).ConfigureAwait(false))
        {
            await WriteSignInRequiredAsync(context, panel).ConfigureAwait(false);
            return;
        }

        // Both tools resolve their assets relative to the trailing slash.
        if (string.Equals(context.Request.Path.Value, panel.PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await Results.LocalRedirect(panel.PathPrefix + "/" + context.Request.QueryString)
                .ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        // Without a session the gateway key came in a header. X-API-Key wins when both are sent
        // (GatewayCredential), which leaves Authorization free for the panel's own token.
        var keyInAuthorization = !hasSession &&
                                 string.IsNullOrWhiteSpace(context.Request.Headers[GatewayCredential.ApiKeyHeader]);
        context.Items[PanelForward.ItemKey] = new PanelForward(panel, keyInAuthorization);

        var error = await services.GetRequiredService<IHttpForwarder>().SendAsync(
            context,
            panel.Upstream.ToString(),
            services.GetRequiredService<PanelUpstreamClient>().Invoker,
            ForwarderRequestConfig.Empty,
            PanelRequestTransformer.Instance).ConfigureAwait(false);

        if (error is ForwarderError.None or ForwarderError.RequestCanceled or ForwarderError.ResponseBodyClient
            or ForwarderError.UpgradeRequestClient or ForwarderError.UpgradeResponseClient)
        {
            return;
        }

        services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(GatewayPanelsEndpoints)).LogWarning(
            context.GetForwarderErrorFeature()?.Exception,
            "Forwarding to the {Panel} panel at {Upstream} failed: {Error}",
            panel.Label,
            panel.Upstream,
            error);

        if (!context.Response.HasStarted)
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                $"{panel.Label} did not answer the gateway at {panel.Upstream}. "
                + "Check that the observability services are running (COMPOSE_PROFILES=observability) "
                + "and that Gateway:Panels points at them.",
                context.RequestAborted).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether a browser says the request was started by a page on another origin.
    /// </summary>
    /// <remarks>
    /// <c>Sec-Fetch-Site</c> where the browser sends it; <c>none</c> is a typed address or a
    /// bookmark. WebSocket handshakes do not carry it in every browser, so <c>Origin</c> is the
    /// fallback there. Host names are compared without the port because TLS is terminated in front
    /// of the gateway.
    /// </remarks>
    private static bool IsFromAnotherOrigin(HttpRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0)
        {
            return site is not ("same-origin" or "none");
        }

        var origin = request.Headers.Origin.ToString();
        return origin.Length > 0 &&
               !(Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                 string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteSignInRequiredAsync(HttpContext context, GatewayPanel panel)
    {
        context.Response.Headers.CacheControl = "no-store";

        // A browser tab gets a page that says where to go; anything else gets the gateway's
        // ordinary 401 body.
        if (context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "text/html; charset=utf-8";
            AdminSecurityHeaders.Apply(context.Response.Headers);
            await context.Response.WriteAsync(
                "<!doctype html><meta charset=\"utf-8\"><title>Sign in required</title>"
                + $"<p>{panel.Label} opens from the Autobahn admin console, and this browser has no panel session "
                + "(it expired, or the gateway restarted).</p>"
                + $"<p><a href=\"/admin/\">Open the console</a> and choose {panel.Label} there.</p>",
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        var response = context.RequestServices.GetRequiredService<IErrorResponseWriter>()
            .Write(GatewayErrorCode.InvalidApiKey);
        context.Response.StatusCode = response.HttpStatusCode;
        context.Response.ContentType = "application/json";
        context.Response.Headers[GatewayHeaders.ErrorCode] = response.Body.Error.Code;
        await context.Response.WriteAsync(response.Json, context.RequestAborted).ConfigureAwait(false);
    }
}
