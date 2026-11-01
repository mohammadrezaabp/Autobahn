using Autobahn.Application.Security;
using Yarp.ReverseProxy.Forwarder;

namespace Autobahn.Host.Panels;
/// <summary>What the request handler decided, read by <see cref = "PanelRequestTransformer"/>.</summary>
/// <param name = "StripAuthorization">
/// True when <c>Authorization</c> carried the gateway key. Otherwise it is left for the panel:
/// Grafana accepts its own service-account tokens there.
/// </param>
internal sealed record PanelForward(GatewayPanel Panel, bool StripAuthorization)
{
    public static readonly object ItemKey = new();
}
