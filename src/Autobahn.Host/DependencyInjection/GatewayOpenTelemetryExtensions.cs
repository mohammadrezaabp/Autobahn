using System.Diagnostics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Autobahn.Domain.Configuration;
using Autobahn.Application.Diagnostics;
using Autobahn.Observability.Metrics;

namespace Autobahn.Host.DependencyInjection;

public static class GatewayOpenTelemetryExtensions
{
    public static IServiceCollection AddGatewayOpenTelemetry(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var telemetry = services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // Managed heap, GC and thread-pool counters. The ASP.NET Core and HttpClient
                // instrumentations above cover request and dependency traffic but say nothing about
                // memory, which leaves the gateway's dominant failure mode unobservable: a large
                // request body is buffered, scanned and forwarded, so heap pressure — not request
                // rate — is what decides whether the process stays inside its container limit. Every
                // symptom of getting that wrong (OOMKill, a GC-bound tail latency, LOH growth under
                // long-context traffic) is invisible without these series.
                .AddRuntimeInstrumentation()
                .AddMeter(GatewayMeters.MeterName)
                .AddMeter(Autobahn.Infrastructure.Fleet.FleetMetrics.MeterName)
                // Seconds-shaped buckets for the two latency histograms. The SDK default boundaries
                // (0, 5, 10, 25 … 10000) are millisecond-shaped, so every inference between half a
                // second and a minute fell into the first two buckets and histogram_quantile() could
                // not tell a 1 s p95 from a 20 s one — the very question "is the model slow or is
                // the gateway queueing?" that this dashboard exists to answer.
                .AddView(
                    "gateway_inference_duration_seconds",
                    new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 20, 30, 60, 120, 300, 600],
                        CardinalityLimit = GatewayMeters.CallerLabelledCardinalityLimit,
                    })
                .AddView(
                    "gateway_time_to_first_token_seconds",
                    new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [0.025, 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 20, 30, 60],
                    })
                // The same boundaries as the total duration, so the two can be compared bucket for
                // bucket when working out what the gateway added.
                .AddView(
                    "gateway_upstream_duration_seconds",
                    new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 20, 30, 60, 120, 300, 600],
                    })
                .AddView(
                    "gateway_bulkhead_queue_wait_seconds",
                    new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 30],
                    })
                // The series that name a caller. The SDK holds a metric to 2,000 points unless told
                // otherwise and folds everything past that into one overflow point, silently — which
                // is the caller labels failing exactly when there are enough callers to need them.
                // MetricCallerBudget is what actually bounds these; this only has to sit above it.
                .AddView(instrument =>
                    instrument.Meter.Name == GatewayMeters.MeterName &&
                    instrument.Name != "gateway_inference_duration_seconds" &&
                    GatewayMeters.CallerLabelledInstruments.Contains(instrument.Name)
                        ? new MetricStreamConfiguration
                        {
                            CardinalityLimit = GatewayMeters.CallerLabelledCardinalityLimit,
                        }
                        : null)
                .AddPrometheusExporter());

        var tracing = configuration?
            .GetSection($"{GatewayOptions.SectionName}:{nameof(GatewayOptions.Tracing)}")
            .Get<GatewayTracingOptions>();
        if (tracing is { IsEnabled: true } && tracing.ResolveExportEndpoint() is { } endpoint)
        {
            telemetry.WithTracing(traces => ConfigureTracing(traces, tracing, endpoint));
        }

        return services;
    }

    /// <summary>
    /// One server span per <c>/v1</c> request and one client span for its forward, exported over
    /// OTLP. ASP.NET Core reads <c>traceparent</c> from the caller, and the upstream HttpClient
    /// writes it on the forwarded request, so a caller's trace runs through the gateway into a
    /// backend that reads the header.
    /// </summary>
    private static void ConfigureTracing(TracerProviderBuilder traces, GatewayTracingOptions tracing, Uri endpoint) =>
        traces
            // Set on the tracer only: the metrics resource feeds target_info on /metrics, which
            // dashboards may already select on.
            .ConfigureResource(resource => resource.AddService(
                string.IsNullOrWhiteSpace(tracing.ServiceName) ? "Autobahn-gateway" : tracing.ServiceName,
                serviceVersion: GatewayBuildInfo.Version))
            .SetSampler(new ParentBasedSampler(
                new TraceIdRatioBasedSampler(Math.Clamp(tracing.SampleRatio, 0d, 1d))))
            // The API callers use, and nothing else: console polling, scrapes and health probes
            // would be most of the spans and none of the questions.
            .AddAspNetCoreInstrumentation(options =>
                options.Filter = static context => IsTraced(context.Request.Path))
            // Only a call made while serving a traced request. The health sweep and the model
            // listing fetches have no parent, and would otherwise each start a trace of their own.
            .AddHttpClientInstrumentation(options =>
                options.FilterHttpRequestMessage = static _ => Activity.Current?.Parent is not null)
            .AddOtlpExporter(options =>
            {
                options.Endpoint = endpoint;
                options.Protocol = tracing.OtlpProtocol == GatewayTracingOptions.Grpc
                    ? OtlpExportProtocol.Grpc
                    : OtlpExportProtocol.HttpProtobuf;
            });

    internal static bool IsTraced(PathString path) =>
        path.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase);
}
