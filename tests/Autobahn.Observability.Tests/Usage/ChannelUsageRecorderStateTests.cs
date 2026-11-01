using Microsoft.Extensions.Logging.Abstractions;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;
using Autobahn.Observability.Metrics;
using Autobahn.Observability.Runtime;
using Autobahn.Observability.Usage;

namespace Autobahn.Observability.Tests.Usage;

public sealed class ChannelUsageRecorderStateTests
{
    private static UsageEvent Event(string id) => new()
    {
        RequestId = id,
        ModelId = "m",
        PromptTokens = 1,
        CompletionTokens = 1,
    };

    [Fact]
    public void QueueDepth_ReflectsEventsNotYetDrained()
    {
        var recorder = new ChannelUsageRecorder(
            Substitute.For<IQuotaService>(),
            Substitute.For<IUsagePersistenceHandler>(),
            Substitute.For<IGatewayMetricsCollector>(),
            NullLogger<ChannelUsageRecorder>.Instance);

        recorder.QueueDepth.Should().Be(0);
        recorder.Enqueue(Event("a")).Should().BeTrue();
        recorder.Enqueue(Event("b")).Should().BeTrue();

        recorder.QueueDepth.Should().Be(2, "the worker has not been started, so nothing drains");
        recorder.Capacity.Should().Be(10_000);
    }

    [Fact]
    public void UsageQualityCounters_TrackParseFailuresEstimatesUnsplitAndDrops()
    {
        var collector = new GatewayMetricsCollector(new GatewayRuntimeState());

        collector.RecordUsageParseFailure("m");
        collector.RecordUsageParseFailure("m");
        collector.RecordEstimatedUsage("m");
        collector.RecordUnsplitUsage("m");
        collector.RecordUsageEventsDropped(3);
        collector.RecordUsageEventsDropped(0);

        IUsageQualityCounters counters = collector;
        counters.ParseFailures.Should().Be(2);
        counters.EstimatedUsage.Should().Be(1);
        counters.UnsplitUsage.Should().Be(1);
        counters.DroppedEvents.Should().Be(3);
    }
}
