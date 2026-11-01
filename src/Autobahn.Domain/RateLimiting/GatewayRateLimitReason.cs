namespace Autobahn.Domain.RateLimiting;
public enum GatewayRateLimitReason
{
    RateLimitExceeded,
    ConcurrencyLimitExceeded,
    TokenLimitExceeded,
}
