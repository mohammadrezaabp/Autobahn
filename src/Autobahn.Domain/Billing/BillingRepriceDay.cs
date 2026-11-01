namespace Autobahn.Domain.Billing;
public sealed record BillingRepriceDay(DateOnly Date, int Events, decimal OldUsd, decimal NewUsd);
