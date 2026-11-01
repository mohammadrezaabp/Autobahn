namespace Autobahn.Domain.RateLimiting;

/// <summary>
/// One token bucket as it stood when it was exported, so a restarted gateway can carry on from the
/// fill the last process left instead of handing every caller a full bucket.
/// </summary>
/// <param name="PartitionKey">The bucket's partition, exactly as the store keys it.</param>
/// <param name="Tokens">Tokens held at <paramref name="AsOfUtc"/>. Negative for a bucket in debt.</param>
/// <param name="Capacity">The capacity the bucket was last held to.</param>
/// <param name="AsOfUtc">When <paramref name="Tokens"/> was read. Refilling resumes from here.</param>
public sealed record RateLimitBucketSnapshot(string PartitionKey, double Tokens, int Capacity, DateTimeOffset AsOfUtc);
