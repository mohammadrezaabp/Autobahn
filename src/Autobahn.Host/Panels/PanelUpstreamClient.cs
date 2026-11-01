using Autobahn.Application.Security;
using Yarp.ReverseProxy.Forwarder;

namespace Autobahn.Host.Panels;
/// <summary>The connection pool shared by the panel proxy and its reachability probe.</summary>
internal sealed class PanelUpstreamClient : IDisposable
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    public HttpMessageInvoker Invoker { get; } = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, // The panels compress their own responses and the browser decodes them.
    AutomaticDecompression = System.Net.DecompressionMethods.None, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(5), });

    public async Task<bool> IsReachableAsync(GatewayPanel panel, CancellationToken cancellationToken)
    {
        var root = panel.StripPrefix ? "/" : panel.PathPrefix + "/";
        using var request = new HttpRequestMessage(HttpMethod.Get, RequestUtilities.MakeDestinationAddress(panel.Upstream.ToString(), new PathString(root + panel.HealthPath), QueryString.Empty));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            using var response = await Invoker.SendAsync(request, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose() => Invoker.Dispose();
}
