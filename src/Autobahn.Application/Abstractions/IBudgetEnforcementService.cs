using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

public interface IBudgetEnforcementService
{
    ValueTask<BudgetCheckResult> CheckBeforeForwardAsync(
        string? tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserves an estimated maximum cost for an in-flight request against the tenant's hard-stop
    /// budgets before forwarding, so concurrent requests (whose actual cost is not yet known) cannot
    /// collectively overshoot a hard cap. Returns a non-allowed result (reserving nothing) when the
    /// reservation would breach a hard-stop budget. Every successful reservation must be released via
    /// <see cref="ReleaseReservation"/> once the request's actual cost has been recorded.
    /// </summary>
    /// <param name="requestBodyBytes">
    /// Size of the prompt being forwarded, used to estimate the input side of the cost. Reserving
    /// only <paramref name="requestedMaxTokens"/> left the input unpriced, and for long-context
    /// traffic the input is the dominant cost — concurrent large-prompt requests could overshoot a
    /// hard cap many times over while each reserved a few thousand output tokens.
    /// </param>
    /// <param name="provider">
    /// Who will serve the request, so the estimate is at that provider's peak price in its peak
    /// hours. Null or a provider with no peak hours prices it at the rate card.
    /// </param>
    ValueTask<BudgetCheckResult> TryReserveAsync(
        string? tenantId,
        string requestId,
        string canonicalModelId,
        long? requestedMaxTokens,
        long requestBodyBytes,
        string? provider,
        CancellationToken cancellationToken = default);

    /// <summary>Releases a reservation taken by <see cref="TryReserveAsync"/> (no-op if unknown).</summary>
    void ReleaseReservation(string requestId);
}
