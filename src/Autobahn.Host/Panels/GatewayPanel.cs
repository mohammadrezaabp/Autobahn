using Microsoft.Extensions.Options;

namespace Autobahn.Host.Panels;
/// <param name = "PathPrefix">Where the panel is served on the gateway, without a trailing slash.</param>
/// <param name = "StripPrefix">
/// Whether <paramref name = "PathPrefix"/> is removed before forwarding. Grafana serves from the
/// sub-path itself (<c>serve_from_sub_path</c>) and receives the path unchanged; Prometheus keeps
/// serving from its root (<c>--web.route-prefix=/</c>), so its scrape, health and datasource URLs
/// are the ones they always were, and only knows the prefix for the links it writes.
/// </param>
/// <param name = "HealthPath">Relative to the panel root.</param>
internal sealed record GatewayPanel(string Id, string Label, string PathPrefix, bool StripPrefix, string HealthPath, Uri Upstream)
{
    /// <summary>The path sent upstream for a request path under <see cref = "PathPrefix"/>.</summary>
    public PathString UpstreamPath(PathString requestPath)
    {
        if (!StripPrefix)
        {
            return requestPath;
        }

        return requestPath.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase, out var rest) && rest.HasValue ? rest : new PathString("/");
    }
}
