using Autobahn.Domain.Configuration;

namespace Autobahn.Domain.RateLimiting;
/// <summary>A tier as the schedule report shows it.</summary>
public sealed record ScheduleTier(int Rpm, int Burst, int MaxConcurrentStreams, bool Suspended)
{
    public int Tpm { get; init; }

    public static ScheduleTier From(RateLimitPolicy policy, bool suspended = false) => new(policy.Rpm, policy.Burst, policy.MaxConcurrentStreams, suspended)
    {
        Tpm = policy.Tpm
    };
}
