namespace Autobahn.Domain.RateLimiting;
/// <summary>
/// What one configured limit did in the window. Every counter is a count of <em>decisions by this
/// limit</em>; none is a count of requests to the gateway.
/// </summary>
/// <param name = "LimitId">
/// Stable id of the control: a rule identity (<c>scope:target</c>, lower case), <c>plan:&lt;slug&gt;</c>
/// or <c>default</c>.
/// </param>
/// <param name = "Scope">The part of <paramref name = "LimitId"/> before the first colon.</param>
/// <param name = "Target">The part after it; empty for <c>default</c>.</param>
/// <param name = "AnonymousBucket">
/// True for the separate bucket a <c>model</c> rule keeps for anonymous callers. Same limit id,
/// different bucket, so it is a row of its own.
/// </param>
/// <param name = "SingleBucket">
/// Whether everything counted here drained one bucket. Only then are <paramref name = "PeakChargedInOneMinute"/>
/// and <paramref name = "PeakUtilization"/> comparable with the limit; a tier's row sums callers that
/// each have a bucket of their own.
/// </param>
/// <param name = "Evaluations">Times this limit was asked for a token. A limit placed after one that refused is not asked.</param>
/// <param name = "Charged">Evaluations where the token was kept: the request passed every rate limit.</param>
/// <param name = "RefusedByRate">Evaluations where this limit's bucket was empty: it is the limit that answered 429.</param>
/// <param name = "PassedThenRefunded">
/// Evaluations where this limit gave a token and got it back because another limit refused.
/// Always <c>Evaluations - Charged - RefusedByRate</c>.
/// </param>
/// <param name = "StreamsStarted">Streaming responses that took a slot under this limit's concurrency cap.</param>
/// <param name = "RefusedByStreams">Streaming requests this limit's concurrency cap refused.</param>
/// <param name = "ChargedPerMinute"><paramref name = "Charged"/> divided by the window length in minutes.</param>
/// <param name = "PeakChargedInOneMinute">The most tokens kept in any one UTC calendar minute of the window.</param>
/// <param name = "PeakMinuteUtc">Start of that minute; null when nothing was charged.</param>
/// <param name = "ConfiguredRpm">
/// Requests per minute this limit last enforced before adaptive scaling — the scheduled tier when a
/// window was active. Zero until a rate decision has been recorded.
/// </param>
/// <param name = "EffectiveRpm">The same after adaptive scaling.</param>
/// <param name = "PeakUtilization">
/// <paramref name = "PeakChargedInOneMinute"/> over <paramref name = "EffectiveRpm"/>. Null unless
/// <paramref name = "SingleBucket"/> and a rate is known. May exceed 1: a bucket also holds burst.
/// </param>
/// <param name = "LastDecisionUtc">The last time this limit was evaluated, to the second. Null after a reset.</param>
public sealed record RateLimitLimitUsageRow(string LimitId, string Scope, string Target, bool AnonymousBucket, bool SingleBucket, long Evaluations, long Charged, long RefusedByRate, long PassedThenRefunded, long StreamsStarted, long RefusedByStreams, double ChargedPerMinute, long PeakChargedInOneMinute, DateTimeOffset? PeakMinuteUtc, int ConfiguredRpm, int EffectiveRpm, double? PeakUtilization, DateTimeOffset? LastDecisionUtc);
