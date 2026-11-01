using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;
/// <summary>
/// Records every admission decision so the gateway can answer "who is using what, and where are they
/// hitting their limits" — per user, per model, and per user-and-model together.
/// </summary>
/// <remarks>
/// <para>In-memory and bounded, deliberately. The gateway is a single process writing to one
/// embedded database, and admission decisions arrive at request rate — persisting one row per
/// decision would put a write on the hot path and make the busiest partition the most expensive one
/// to meter. Instead each tracked key keeps a fixed ring of per-minute counters, so a read is
/// O(keys) and a write is O(1) with no allocation, and the memory is the same whether the gateway is
/// idle or saturated.</para>
///
/// <para>The trade is that the report resets with the process and looks back hours rather than
/// months. Long-horizon usage already has a home — the billing rollups, which are durable and record
/// tokens and cost per tenant and model. This tracker answers the question those cannot: how each
/// caller sits against its <em>limits</em> right now.</para>
/// </remarks>
public interface IRateLimitUsageTracker
{
    /// <summary>Records one decision. Called from the request path, so it must not allocate or block.</summary>
    void Record(in RateLimitUsageEvent usageEvent);
    /// <summary>
    /// Builds the usage report over the last <paramref name = "minutes"/> minutes.
    /// </summary>
    /// <param name = "take">Maximum rows per section, ordered by request volume.</param>
    RateLimitUsageReport BuildReport(int minutes, int take, DateTimeOffset now);
    /// <summary>Drops every counter. Used by the admin "reset stats" action.</summary>
    void Reset();
    /// <summary>
    /// Records what one rate stage did to each configured limit in <paramref name = "rules"/>, for the
    /// per-limit section of the report. Called from the request path: no allocation, no shared lock.
    /// </summary>
    /// <param name = "rules">The rule set exactly as it was handed to the store.</param>
    /// <param name = "outcome">How the stage ended for this request.</param>
    /// <param name = "refusedPartitionKey">
    /// With <see cref = "RateLimitStageOutcome.Refused"/>, the bucket that refused. Rules before it
    /// were passed and refunded, rules after it were never asked.
    /// </param>
    void RecordRateStage(ReadOnlySpan<RateLimitRule> rules, RateLimitStageOutcome outcome, string? refusedPartitionKey = null)
    {
    }

    /// <summary>
    /// Records a stream-slot decision against the limits that cap concurrency: one stream started
    /// under each, or one refusal under the cap that was full.
    /// </summary>
    void RecordStreamStage(ReadOnlySpan<RateLimitRule> rules, string? refusedPartitionKey = null)
    {
    }

    /// <summary>
    /// Records one step of the failed-credential limiter, which is enforced outside the rule set and
    /// so has no <see cref = "RateLimitRule"/> to describe it.
    /// </summary>
    void RecordAuthFailure(RateLimitAuthFailureStep step, int enforcedRpm)
    {
    }

    /// <summary>
    /// Per-minute points over the last <paramref name = "minutes"/>, gateway-wide or for one limit.
    /// Null when <paramref name = "limitId"/> names a limit the tracker holds nothing for.
    /// </summary>
    RateLimitUsageSeries? BuildSeries(int minutes, int bucketMinutes, string? limitId, bool anonymousBucket, DateTimeOffset now) => null;
    /// <summary>
    /// How close each limit that drains one bucket is to its rate, over the last
    /// <paramref name = "minutes"/> complete minutes. Tiers are left out: every caller of a tier has a
    /// bucket of its own, so their sum is not comparable with the limit.
    /// </summary>
    IReadOnlyList<RateLimitLimitUtilization> ReadLimitUtilization(int minutes, DateTimeOffset now) => [];
}
