namespace Autobahn.Domain.RateLimiting;
/// <param name = "Requests">Admission decisions made in the window.</param>
/// <param name = "Admitted">Decisions that let the request through.</param>
/// <param name = "Rejected">Decisions that refused it.</param>
/// <param name = "RateRejected">Refusals from a token bucket.</param>
/// <param name = "ConcurrencyRejected">Refusals from a concurrency cap.</param>
public sealed record RateLimitUsageTotals(long Requests, long Admitted, long Rejected, long RateRejected, long ConcurrencyRejected)
{
    /// <summary>Share of decisions that were refusals, in <c>[0, 1]</c>.</summary>
    public double RejectionRate => Requests == 0 ? 0 : (double)Rejected / Requests;
}
