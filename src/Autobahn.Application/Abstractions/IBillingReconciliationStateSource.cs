using Autobahn.Domain.Models.Overview;

namespace Autobahn.Application.Abstractions;

/// <summary>The outcome of the most recent billing reconciliation sweep, for the admin Overview.</summary>
public interface IBillingReconciliationStateSource
{
    ReconciliationStatus Current { get; }
}
