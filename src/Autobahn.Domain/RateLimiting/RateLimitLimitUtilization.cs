namespace Autobahn.Domain.RateLimiting;
/// <summary>How much of one limit's sustained rate was used over the last few complete minutes.</summary>
/// <param name = "LimitId">The limit, as <see cref = "RateLimitLimitUsageRow.LimitId"/> spells it.</param>
/// <param name = "Ratio">
/// Tokens kept per minute over the rate enforced. Can pass 1 for a short while: a bucket also holds
/// burst.
/// </param>
public readonly record struct RateLimitLimitUtilization(string LimitId, double Ratio);
