namespace Autobahn.Domain.Billing;
/// <param name = "TenantId">Null for keyless traffic.</param>
public sealed record BillingRepriceTenant(Guid? TenantId, int Events, decimal OldUsd, decimal NewUsd);
