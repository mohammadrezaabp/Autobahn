namespace Autobahn.Domain.Configuration;

/// <summary>Where request traces are sent. Tracing is off until an endpoint is set.</summary>
public sealed class GatewayTracingOptions
{
    public const string Grpc = "grpc";

    public const string HttpProtobuf = "http/protobuf";

    /// <summary>
    /// OTLP endpoint of the collector, for example <c>http://tempo:4318</c>. Empty turns tracing off:
    /// no span is recorded and nothing is exported. With <c>http/protobuf</c> an endpoint that names
    /// no path is sent to <c>/v1/traces</c> on it.
    /// </summary>
    public string OtlpEndpoint { get; set; } = string.Empty;

    /// <summary><c>http/protobuf</c> or <c>grpc</c>.</summary>
    /// <remarks>
    /// HTTP is the default because it works over HTTP/1.1. gRPC needs HTTP/2 from the process's
    /// HttpClient, which the compose stack switches off for the upstream connections
    /// (<c>DOTNET_SYSTEM_NET_HTTP_SOCKETSHTTPHANDLER_HTTP2SUPPORT=false</c>); with that set a gRPC
    /// export fails without a word and no trace arrives.
    /// </remarks>
    public string OtlpProtocol { get; set; } = HttpProtobuf;

    /// <summary>
    /// Where the spans are posted: <see cref="OtlpEndpoint"/>, with <c>/v1/traces</c> added for
    /// <c>http/protobuf</c> when the endpoint names no path. Null when the endpoint is not a URL.
    /// </summary>
    public Uri? ResolveExportEndpoint()
    {
        if (!Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out var endpoint))
        {
            return null;
        }

        return OtlpProtocol == HttpProtobuf && endpoint.AbsolutePath is "" or "/"
            ? new Uri(endpoint, "/v1/traces")
            : endpoint;
    }

    /// <summary>
    /// Share of requests traced when the caller sent no <c>traceparent</c>, from 0 to 1. A request
    /// that carries one follows the caller's own sampling decision.
    /// </summary>
    public double SampleRatio { get; set; } = 1.0;

    /// <summary>The <c>service.name</c> the spans are reported under.</summary>
    public string ServiceName { get; set; } = "Autobahn-gateway";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(OtlpEndpoint);
}
