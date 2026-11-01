namespace Autobahn.Domain.RateLimiting;
/// <param name = "RequestPartitions">Live token buckets.</param>
/// <param name = "StreamPartitions">Live concurrency-slot states.</param>
/// <param name = "MaxPartitions">The ceiling each dimension is held to; approaching it means evictions.</param>
public sealed record RateLimitStoreReport(int RequestPartitions, int StreamPartitions, int MaxPartitions);
