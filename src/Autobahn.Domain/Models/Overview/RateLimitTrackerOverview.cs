namespace Autobahn.Domain.Models.Overview;
/// <param name = "IsSaturated">At least one decision was not counted because a dimension was full; a missing row is then unknown, not zero.</param>
public sealed record RateLimitTrackerOverview(bool IsSaturated = false, long DroppedDecisions = 0, DateTimeOffset? FirstDroppedUtc = null, int MaxKeysPerDimension = 0)
{
    /// <summary>Dimensions that cannot take a new key (tenants, models, apiKeys, tenantModels, violations, limits).</summary>
    public IReadOnlyList<string> AtCapacity { get; init; } = [];
}
