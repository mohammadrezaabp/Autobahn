namespace Autobahn.Domain.Models.Overview;
/// <summary>
/// Why the gateway refused a request before (or instead of) forwarding it. The windowed statistics
/// behind the Overview count rejections per reason so the policy card can say <em>which</em> control
/// is biting, not just that one is.
/// </summary>
public enum RejectionReason
{
    RateLimit = 0,
    Quota = 1,
    Budget = 2,
    Bulkhead = 3,
    StreamConcurrency = 4,
    GrantDenied = 5,
    ModelNotFound = 6,
    BackendUnhealthy = 7,
    CircuitOpen = 8,
    ModelStopped = 9,
}
