using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Proxy.DependencyInjection;
using Autobahn.Proxy.Forwarding;

namespace Autobahn.Proxy.Tests.DependencyInjection;

public sealed class ProxyServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGatewayProxy_RegistersHttpForwarderAndMessageInvoker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new GatewayOptions()));
        services.AddSingleton<IGatewayMetricsCollector, NoOpGatewayMetricsCollector>();
        services.AddHttpClient(Autobahn.Application.Http.UpstreamHttpClientNames.Inference);
        services.AddGatewayProxy();
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<Yarp.ReverseProxy.Forwarder.IHttpForwarder>().Should().NotBeNull();
        provider.GetRequiredService<IInferenceHttpForwarder>().Should().NotBeNull();
    }

    private sealed class NoOpGatewayMetricsCollector : IGatewayMetricsCollector
    {
        public void RecordRateLimitRejection(string reason) { }
        public void RecordQuotaRejection() { }
        public void RecordTokenUsage(string modelId, long promptTokens, long completionTokens) { }
        public void RecordEstimatedUsage(string modelId)
        {
        }

        public void RecordUnsplitUsage(string modelId)
        {
        }

        public void RecordUsageParseFailure(string modelId) { }
        public void RecordInferenceRouted(string modelId, string route, bool isStreaming) { }
        public void RecordForwardAttempt(string modelId, string outcome) { }
        public void RecordModelResolve(string result) { }
        public void RecordCircuitBreakerTransition(string modelId, string toState) { }
        public void RecordBulkheadRejection(string modelId) { }
        public void RecordBulkheadInflightChange(string modelId, int delta) { }
        public void RecordTimeToFirstToken(string modelId, double seconds) { }

        public void RecordBillingReconciliation(int discrepancyCount, double absoluteCostDrift) { }
    }
}
