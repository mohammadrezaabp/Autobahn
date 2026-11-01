using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;

/// <summary>
/// The rate-limit store's token buckets, as something that can be written down and read back.
/// </summary>
/// <remarks>
/// Separate from <see cref="IDistributedRateLimitStore"/> because only a store that keeps its
/// buckets in the process has anything to lose on a restart. Concurrency slots are not part of it:
/// the streams that held them end with the process.
/// </remarks>
public interface IRateLimitBucketSnapshotSource
{
    /// <summary>
    /// The buckets that hold fewer tokens than their capacity at <paramref name="now"/>. A full
    /// bucket is left out: a caller with no bucket starts with a full one anyway.
    /// </summary>
    /// <param name="maxBuckets">Upper bound on the rows returned.</param>
    IReadOnlyList<RateLimitBucketSnapshot> ExportSpentBuckets(DateTimeOffset now, int maxBuckets);

    /// <summary>
    /// Seats <paramref name="buckets"/> in the store. A partition that already has a bucket keeps
    /// it, and a snapshot older than the store's retention is ignored.
    /// </summary>
    /// <returns>How many buckets were seated.</returns>
    int RestoreBuckets(IReadOnlyList<RateLimitBucketSnapshot> buckets, DateTimeOffset now);
}
