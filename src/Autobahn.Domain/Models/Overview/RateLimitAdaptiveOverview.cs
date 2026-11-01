namespace Autobahn.Domain.Models.Overview;
/// <param name = "ModelsReduced">Models whose factor is below 1, i.e. being enforced below their configured rate.</param>
/// <param name = "LowestFactor">The smallest factor in force; null when no model is reduced.</param>
public sealed record RateLimitAdaptiveOverview(bool Enabled = false, int ModelsReduced = 0, double? LowestFactor = null, string? LowestFactorModelId = null, int BackedOffPartitions = 0, DateTimeOffset? LastEvaluatedUtc = null)
{
    /// <summary>Load shedding is actively reducing at least one model's rate.</summary>
    public bool Shedding => Enabled && ModelsReduced > 0;
}
