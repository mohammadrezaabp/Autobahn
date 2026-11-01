namespace Autobahn.Domain.RateLimiting;
/// <summary>Per-minute history from the same counters as the usage report.</summary>
/// <param name = "Subject"><c>gateway</c>, or <c>limit</c> when <paramref name = "LimitId"/> is set.</param>
/// <param name = "LimitId">The limit the points describe; null for the gateway-wide series.</param>
/// <param name = "AnonymousBucket">Which of a model rule's two buckets; false otherwise.</param>
/// <param name = "BucketMinutes">Width of every point. Buckets are aligned to the Unix epoch in UTC.</param>
/// <param name = "FromUtc">Start of the first bucket.</param>
/// <param name = "ToUtc">End of the last bucket, which is the one in progress.</param>
/// <param name = "TrackingSinceUtc">Counting began here. Buckets that end before it are not covered.</param>
/// <param name = "Points">Oldest first, contiguous, at most 180.</param>
public sealed record RateLimitUsageSeries(string Subject, string? LimitId, bool AnonymousBucket, int BucketMinutes, DateTimeOffset FromUtc, DateTimeOffset ToUtc, DateTimeOffset? TrackingSinceUtc, IReadOnlyList<RateLimitUsagePoint> Points);
