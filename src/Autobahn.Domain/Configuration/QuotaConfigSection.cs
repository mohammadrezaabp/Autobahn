using Autobahn.Domain.RateLimiting;

namespace Autobahn.Domain.Configuration;
/// <summary>
/// Quota section of the config snapshot: the runtime-tunable per-partition monthly token limit and
/// the soft-warning ratio, read per request by the quota service. The defaults mirror
/// <see cref = "QuotaOptions"/> so a database-less deployment (and the pre-load window) behaves exactly
/// as it did when these values were read straight from appsettings. Non-tunable scalars
/// (<c>CommittedRequestIdRetentionLimit</c>, resilience knobs, the key pepper) stay in appsettings.
/// </summary>
public sealed record QuotaConfigSection
{
    public long DefaultMonthlyTokenLimit { get; init; } = 1_000_000;
    public double SoftLimitRatio { get; init; } = 0.9;
    public static QuotaConfigSection Defaults { get; } = new();
}
