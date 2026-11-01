using Autobahn.Application.Security;
using Yarp.ReverseProxy.Forwarder;

namespace Autobahn.Host.Panels;
internal sealed class PanelRequestTransformer : HttpTransformer
{
    public static readonly PanelRequestTransformer Instance = new();
    public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
    {
        await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken).ConfigureAwait(false);
        var forward = (PanelForward)httpContext.Items[PanelForward.ItemKey]!;
        proxyRequest.RequestUri = RequestUtilities.MakeDestinationAddress(destinationPrefix, forward.Panel.UpstreamPath(httpContext.Request.Path), httpContext.Request.QueryString);
        // The browser's Host, not the container's. Grafana compares it with Origin before it
        // accepts a write, and both panels build absolute redirects from it.
        proxyRequest.Headers.Host = httpContext.Request.Host.Value;
        // Gateway credentials stop here: the panels have no use for them.
        proxyRequest.Headers.Remove(GatewayCredential.ApiKeyHeader);
        if (forward.StripAuthorization)
        {
            proxyRequest.Headers.Remove("Authorization");
        }

        var cookies = WithoutSessionCookie(httpContext.Request.Headers.Cookie.ToString());
        proxyRequest.Headers.Remove("Cookie");
        if (cookies.Length > 0)
        {
            proxyRequest.Headers.TryAddWithoutValidation("Cookie", cookies);
        }
    }

    /// <summary>The <c>Cookie</c> header minus the panel session, every other pair untouched.</summary>
    internal static string WithoutSessionCookie(string cookieHeader)
    {
        if (cookieHeader.Length == 0)
        {
            return cookieHeader;
        }

        var kept = cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(pair => !pair.StartsWith(PanelSessionTokens.CookieName + "=", StringComparison.Ordinal));
        return string.Join("; ", kept);
    }
}
