namespace Autobahn.Application.Abstractions;
/// <summary>Per-model bulkhead occupancy for the admin Overview: how full each model's forwarding slots and wait queue are.</summary>
public interface IBulkheadStateSource
{
    IReadOnlyList<BulkheadModelState> GetStates();
}
