namespace Autobahn.Domain.RateLimiting;
/// <param name = "Key">The row's identity — a tenant id, a model id, an API key id, or "tenant|model".</param>
/// <param name = "TenantId">The tenant, when the row has one.</param>
/// <param name = "ApiKeyId">The API key, when the row has one.</param>
/// <param name = "ModelId">The model, when the row has one.</param>
/// <param name = "Requests">Admission decisions in the window.</param>
/// <param name = "Admitted">How many were let through.</param>
/// <param name = "Rejected">How many were refused.</param>
/// <param name = "RequestsPerMinute">Observed rate over the window — the "load" column.</param>
/// <param name = "ConfiguredRpm">
/// The tier this row is held to, when one scope clearly owns it. Zero when the row aggregates rows
/// governed by different tiers, where a single limit number would be a fiction.
/// </param>
/// <param name = "EffectiveRpm">What was enforced after adaptation; equal to <paramref name = "ConfiguredRpm"/> when nothing was adapted.</param>
public sealed record RateLimitUsageRow(string Key, string? TenantId, string? ApiKeyId, string? ModelId, long Requests, long Admitted, long Rejected, double RequestsPerMinute, int ConfiguredRpm, int EffectiveRpm)
{
    /// <summary>
    /// The most decisions in any one UTC calendar minute of the window. Attempts, like
    /// <see cref = "Requests"/>: for a row that was being refused it includes the retries. This is the
    /// number a cap has to clear, where <see cref = "RequestsPerMinute"/> is an average that a bursty
    /// caller sits far above at its busiest.
    /// </summary>
    public long PeakRequestsInOneMinute { get; init; }
    /// <summary>Start of that minute; null when the row has no decisions.</summary>
    public DateTimeOffset? PeakMinuteUtc { get; init; }
    /// <summary>
    /// Observed rate as a share of the limit in force, in <c>[0, 1+]</c>. Null when the row has no
    /// single governing limit. Above 1 is normal and expected for a row that is being refused: the
    /// numerator counts attempts, not admissions.
    /// </summary>
    public double? Utilization => EffectiveRpm <= 0 ? null : RequestsPerMinute / EffectiveRpm;
}
