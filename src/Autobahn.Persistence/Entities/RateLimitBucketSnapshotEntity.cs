namespace Autobahn.Persistence.Entities;

/// <summary>
/// One spent rate-limit token bucket, written on an interval so a restarted gateway carries on from
/// it. Only buckets below capacity have a row.
/// </summary>
public sealed class RateLimitBucketSnapshotEntity
{
    public required string PartitionKey { get; set; }

    /// <summary>Tokens held at <see cref="AsOfUtc"/>; negative for a bucket in debt.</summary>
    public double Tokens { get; set; }

    public int Capacity { get; set; }

    public DateTimeOffset AsOfUtc { get; set; }
}
