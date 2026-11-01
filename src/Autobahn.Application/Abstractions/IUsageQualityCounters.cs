namespace Autobahn.Application.Abstractions;
/// <summary>
/// Process-lifetime counters for the usage-quality signals the metrics collector otherwise only
/// exports to Prometheus. Read by the Overview; written by <c>IGatewayMetricsCollector</c>.
/// </summary>
public interface IUsageQualityCounters
{
    long ParseFailures { get; }

    long EstimatedUsage { get; }

    long UnsplitUsage { get; }

    /// <summary>Usage events that never reached the ledger (queue saturated or retries exhausted).</summary>
    long DroppedEvents { get; }
}
