namespace Autobahn.Domain.RateLimiting;
/// <param name = "Enabled">Whether load-aware adaptation is switched on.</param>
/// <param name = "LastEvaluatedUtc">When factors were last recomputed.</param>
/// <param name = "BackedOffPartitions">Partitions currently being told to wait longer than the bucket alone would say.</param>
/// <param name = "Models">Per-model factor, saturation and the reason it last moved.</param>
public sealed record AdaptiveRateLimitReport(bool Enabled, DateTimeOffset? LastEvaluatedUtc, int BackedOffPartitions, IReadOnlyList<AdaptiveModelRow> Models);
