namespace Autobahn.Domain.Billing;
/// <summary>The new price of one ledger row.</summary>
public sealed record BillingEventCostUpdate(Guid EventId, decimal InputCost, decimal OutputCost, decimal TotalCost);
