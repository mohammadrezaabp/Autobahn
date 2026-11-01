namespace Autobahn.Domain.Models.Overview;
/// <param name = "Scope"><c>auth_failure</c> or <c>anonymous</c>.</param>
public sealed record RateLimitProtectiveOverview(string Scope, long Checked, long Refused, int EnforcedRpm, DateTimeOffset? LastDecisionUtc);
