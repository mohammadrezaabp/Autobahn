using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;
/// <summary>How one rate stage ended, from the point of view of the limits in it.</summary>
public enum RateLimitStageOutcome
{
    /// <summary>Every limit gave a token and the request went on: each one was charged.</summary>
    Charged = 0,
    /// <summary>One limit in this stage refused; the ones before it were refunded.</summary>
    Refused = 1,
    /// <summary>Every limit here gave a token, then a later stage refused and these were refunded.</summary>
    RefundedByLaterStage = 2,
}
