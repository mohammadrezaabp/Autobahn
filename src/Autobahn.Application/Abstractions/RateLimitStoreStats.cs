using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;
/// <param name = "RequestPartitions">Live token buckets.</param>
/// <param name = "StreamPartitions">Live concurrency-slot states.</param>
/// <param name = "MaxPartitions">The configured ceiling each dimension is held to.</param>
/// <param name = "ForcedEvictions">
/// Partitions evicted since start with budget still spent, each of which starts full again on its
/// next request.
/// </param>
/// <remarks>
/// <see cref = "ForcedEvictions"/> is the one number here that says enforcement has stopped being
/// correct rather than merely being busy: past the partition ceiling the sweep has to drop buckets
/// that were still holding a caller to its tier, and dropping one hands that caller a full budget it
/// did not earn. It was reported only as a log line, emitted once when the condition started — so the
/// single state in which limits are silently not enforced was the one state nothing could alert on.
/// </remarks>
public readonly record struct RateLimitStoreStats(int RequestPartitions, int StreamPartitions, int MaxPartitions, long ForcedEvictions = 0);
