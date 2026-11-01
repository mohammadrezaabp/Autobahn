namespace Autobahn.Domain.RateLimiting;
/// <summary>Activity of one protective scope in the window. Every caller has its own bucket.</summary>
/// <remarks>
/// The <c>anonymous</c> row counts only while the anonymous rule sets a rate of its own. Without one,
/// anonymous callers are held to the default tier and are counted under the <c>default</c> limit.
/// </remarks>
/// <param name = "Scope"><c>auth_failure</c> or <c>anonymous</c>.</param>
/// <param name = "LimitId">The scope's rule identity.</param>
/// <param name = "Checked">
/// <c>auth_failure</c>: credentialed requests checked against an address block's failed-credential
/// budget. <c>anonymous</c>: anonymous requests evaluated against the anonymous rate.
/// </param>
/// <param name = "Charged">
/// <c>auth_failure</c>: credentials that were then rejected, each debiting one token.
/// <c>anonymous</c>: requests that kept their token.
/// </param>
/// <param name = "Refused">Requests this scope answered 429.</param>
/// <param name = "EnforcedRpm">
/// The rate last enforced. For <c>auth_failure</c> with no rule configured this is the default
/// tier's rate, which is what the limiter falls back to. Zero until a decision is recorded.
/// </param>
/// <param name = "RefusedByStreams"><c>anonymous</c> only: streaming requests its concurrency cap refused.</param>
/// <param name = "LastDecisionUtc">The last decision, to the second. Null when there has been none.</param>
public sealed record RateLimitProtectiveUsageRow(string Scope, string LimitId, long Checked, long Charged, long Refused, int EnforcedRpm, long RefusedByStreams, DateTimeOffset? LastDecisionUtc);
