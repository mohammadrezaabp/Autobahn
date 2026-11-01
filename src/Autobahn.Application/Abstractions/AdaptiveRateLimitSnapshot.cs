using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;
/// <param name = "Enabled">Whether adaptation is switched on.</param>
/// <param name = "Models">One row per model the governor is currently tracking.</param>
/// <param name = "BackedOffPartitions">Partitions currently serving an escalated <c>Retry-After</c>.</param>
/// <param name = "LastEvaluatedUtc">When the last evaluation ran; null before the first one.</param>
public sealed record AdaptiveRateLimitSnapshot(bool Enabled, IReadOnlyList<AdaptiveModelState> Models, int BackedOffPartitions, DateTimeOffset? LastEvaluatedUtc)
{
    public static AdaptiveRateLimitSnapshot Disabled { get; } = new(false, [], 0, null);
}
