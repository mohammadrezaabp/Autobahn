namespace Autobahn.Domain.RateLimiting;
/// <param name = "StartUtc">Start of the bucket, UTC.</param>
/// <param name = "Covered">
/// False when the bucket ended before counting began, so its zeros are "not observed" rather than
/// "nothing happened". A covered bucket with zeros is a real zero.
/// </param>
/// <param name = "Decisions">Gateway: admission decisions. Limit: evaluations of that limit.</param>
/// <param name = "Admitted">Gateway: decisions that admitted. Limit: tokens kept (charged).</param>
/// <param name = "RefusedByRate">Refusals from a token bucket.</param>
/// <param name = "RefusedByStreams">Refusals from a concurrency cap.</param>
public sealed record RateLimitUsagePoint(DateTimeOffset StartUtc, bool Covered, long Decisions, long Admitted, long RefusedByRate, long RefusedByStreams);
