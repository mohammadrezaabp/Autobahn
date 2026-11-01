namespace Autobahn.Domain.Configuration;
/// <summary>
/// Thrown when a rate-limit write was based on a configuration version that is no longer current.
/// </summary>
/// <remarks>
/// The rule set is replaced wholesale — a partial update gives no way to delete a rule — so a write
/// based on a stale read does not merge with what landed in between, it erases it. Two operators with
/// the Rate limits page open would each save their own complete set and the second would win
/// silently, including for security-relevant rules like <c>auth_failure</c>. The same shape as
/// <see cref = "Autobahn.Domain.Models.ModelRouteVersionConflictException"/>, which guards the route table
/// against exactly this.
/// </remarks>
public sealed class RateLimitVersionConflictException(long expectedVersion, long actualVersion) : InvalidOperationException($"Rate-limit configuration changed since it was read (expected version {expectedVersion}, " + $"found {actualVersion}).")
{
    public long ExpectedVersion { get; } = expectedVersion;
    public long ActualVersion { get; } = actualVersion;
}
