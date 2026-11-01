namespace Autobahn.Domain.Billing;

public sealed record BillingCostBreakdown(
    decimal InputCost,
    decimal OutputCost,
    decimal TotalCost,
    string Currency);
