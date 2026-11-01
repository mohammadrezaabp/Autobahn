using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;

namespace Autobahn.Billing.Usage;

public sealed class NoOpBudgetEnforcementService : IBudgetEnforcementService
{
    public ValueTask<BudgetCheckResult> CheckBeforeForwardAsync(
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        _ = tenantId;
        _ = cancellationToken;
        return ValueTask.FromResult(BudgetCheckResult.Allowed);
    }

    public ValueTask<BudgetCheckResult> TryReserveAsync(
        string? tenantId,
        string requestId,
        string canonicalModelId,
        long? requestedMaxTokens,
        long requestBodyBytes,
        string? provider,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(BudgetCheckResult.Allowed);

    public void ReleaseReservation(string requestId)
    {
    }
}
