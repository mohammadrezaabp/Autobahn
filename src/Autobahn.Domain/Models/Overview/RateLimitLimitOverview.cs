namespace Autobahn.Domain.Models.Overview;
/// <summary>One configured limit's own decisions in the last hour.</summary>
/// <param name = "LimitId">Stable id: a rule identity (<c>scope:target</c>, lower case), <c>plan:&lt;slug&gt;</c> or <c>default</c>.</param>
/// <param name = "RuleId">
/// <paramref name = "LimitId"/> when the limit is a scoped rule that can be opened in the rule list; null
/// for plan tiers and the default tier, which are edited as baselines.
/// </param>
/// <param name = "SingleBucket">Whether everything counted drained one bucket; only then is <paramref name = "PeakUtilization"/> meaningful.</param>
/// <param name = "Refused"><paramref name = "RefusedByRate"/> plus <paramref name = "RefusedByStreams"/>.</param>
/// <param name = "PeakUtilization">
/// Busiest minute over the enforced rate. Null unless the limit counts a single bucket and a rate is
/// known — a tier's row sums many callers' buckets and has no meaningful percentage.
/// </param>
public sealed record RateLimitLimitOverview(string LimitId, string? RuleId, string Scope, string Target, bool AnonymousBucket, bool SingleBucket, long Evaluations, long Refused, long RefusedByRate, long RefusedByStreams, int ConfiguredRpm, int EffectiveRpm, double? PeakUtilization, DateTimeOffset? LastDecisionUtc)
{
    /// <summary>
    /// Refused nothing, but the busiest minute reached <see cref = "RateLimitOverviewLimits.NearRatio"/>
    /// of the enforced rate. Only possible where <see cref = "PeakUtilization"/> exists (a single bucket).
    /// </summary>
    public bool NearLimit => Refused == 0 && PeakUtilization >= RateLimitOverviewLimits.NearRatio;
    /// <summary>
    /// Display name for a key-scoped limit's target — the key's label or public prefix (then the model
    /// for a pair), or "unknown key". Null for other scopes, whose target is already readable. Display
    /// only: navigate by <see cref = "RuleId"/>.
    /// </summary>
    public string? TargetLabel { get; init; }
}
