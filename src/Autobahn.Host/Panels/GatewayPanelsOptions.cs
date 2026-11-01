namespace Autobahn.Host.Panels;

/// <summary>
/// The monitoring panels the gateway serves under <c>/admin</c> on its own port: Grafana at
/// <c>/admin/grafana/</c> and Prometheus at <c>/admin/prometheus/</c>. Bound from
/// <c>Gateway:Panels</c>; the compose stack maps <c>GATEWAY_PANELS_GRAFANA_URL</c> and
/// <c>GATEWAY_PANELS_PROMETHEUS_URL</c> onto it.
/// </summary>
/// <remarks>
/// Both tools are published on loopback only, so on a host whose one reachable port is the
/// gateway's they could be opened only through an SSH tunnel. Forwarding them from here puts them
/// behind the same Operator check as the rest of the control plane instead of opening two more
/// ports, one of which (Prometheus) has no authentication of its own.
/// </remarks>
public sealed class GatewayPanelsOptions
{
    public const string SectionName = "Gateway:Panels";

    /// <summary>Where the gateway reaches Grafana, e.g. <c>http://grafana:3000</c>. Empty disables the panel.</summary>
    public string? GrafanaUrl { get; set; }

    /// <summary>Where the gateway reaches Prometheus, e.g. <c>http://prometheus:9090</c>. Empty disables the panel.</summary>
    public string? PrometheusUrl { get; set; }

    /// <summary>
    /// The Grafana dashboard the console's <b>Logs</b> link opens, relative to Grafana's root, e.g.
    /// <c>d/Autobahn-logs/Autobahn-logs</c>. Empty for no link: the gateway cannot tell whether the
    /// Grafana it forwards to has that dashboard, so the deployment that ships it says so.
    /// </summary>
    public string? LogsDashboard { get; set; }

    /// <summary>
    /// How long a panel session opened from the console lasts. A revoked key keeps the panels it
    /// already opened for at most this long, so it is a working day rather than a week.
    /// </summary>
    public int SessionMinutes { get; set; } = 480;

    internal static bool IsUsableUpstream(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
         (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}
