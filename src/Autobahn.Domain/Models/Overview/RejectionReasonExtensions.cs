namespace Autobahn.Domain.Models.Overview;
public static class RejectionReasonExtensions
{
    /// <summary>The wire label used in the summary JSON and matching the Prometheus <c>reason</c> tag.</summary>
    public static string ToLabel(this RejectionReason reason) => reason switch
    {
        RejectionReason.RateLimit => "rate_limit",
        RejectionReason.Quota => "quota",
        RejectionReason.Budget => "budget",
        RejectionReason.Bulkhead => "bulkhead",
        RejectionReason.StreamConcurrency => "stream_concurrency",
        RejectionReason.GrantDenied => "grant_denied",
        RejectionReason.ModelNotFound => "model_not_found",
        RejectionReason.BackendUnhealthy => "backend_unhealthy",
        RejectionReason.CircuitOpen => "circuit_open",
        RejectionReason.ModelStopped => "model_stopped",
        _ => reason.ToString().ToLowerInvariant(),
    };
}
