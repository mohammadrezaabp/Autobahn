using Microsoft.Extensions.Options;

namespace Autobahn.Host.Panels;
internal sealed class GatewayPanelCatalog
{
    public const string GrafanaId = "grafana";
    public const string GrafanaPrefix = "/admin/grafana";
    public const string PrometheusPrefix = "/admin/prometheus";
    /// <summary>Every prefix a panel can be served from, configured or not.</summary>
    public static readonly string[] PathPrefixes = [GrafanaPrefix, PrometheusPrefix];
    public GatewayPanelCatalog(IOptions<GatewayPanelsOptions> options)
    {
        var settings = options.Value;
        var panels = new List<GatewayPanel>(2);
        Add(panels, GrafanaId, "Grafana", GrafanaPrefix, stripPrefix: false, "api/health", settings.GrafanaUrl);
        Add(panels, "prometheus", "Prometheus", PrometheusPrefix, stripPrefix: true, "-/healthy", settings.PrometheusUrl);
        Panels = panels;
    }

    public IReadOnlyList<GatewayPanel> Panels { get; }

    public GatewayPanel? Find(PathString path)
    {
        foreach (var panel in Panels)
        {
            if (path.StartsWithSegments(panel.PathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return panel;
            }
        }

        return null;
    }

    public static bool IsPanelPath(PathString path)
    {
        foreach (var prefix in PathPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Add(List<GatewayPanel> panels, string id, string label, string prefix, bool stripPrefix, string healthPath, string? upstream)
    {
        if (!string.IsNullOrWhiteSpace(upstream))
        {
            panels.Add(new GatewayPanel(id, label, prefix, stripPrefix, healthPath, new Uri(upstream.Trim())));
        }
    }
}
