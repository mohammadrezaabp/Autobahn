namespace Autobahn.Domain.RateLimiting;
/// <summary>Completeness of the in-memory counters. Process-local; resets with the process.</summary>
/// <param name = "TrackingSinceUtc">When counting started: process start or the last reset.</param>
/// <param name = "MaxKeysPerDimension">The ceiling each dimension is held to.</param>
/// <param name = "IsSaturated">
/// True when at least one decision since <paramref name = "TrackingSinceUtc"/> was not counted because
/// a dimension was full. Absence of a row then does not mean absence of traffic.
/// </param>
/// <param name = "Dimensions">One entry per section of the report.</param>
public sealed record RateLimitTrackerReport(DateTimeOffset? TrackingSinceUtc, int MaxKeysPerDimension, bool IsSaturated, IReadOnlyList<RateLimitTrackerDimension> Dimensions)
{
    public static RateLimitTrackerReport Empty { get; } = new(null, 0, false, []);
}
