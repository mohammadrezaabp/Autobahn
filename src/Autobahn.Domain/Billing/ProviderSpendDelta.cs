namespace Autobahn.Domain.Billing;
/// <summary>An addition to one <see cref = "ProviderSpendRecord"/> bucket.</summary>
public sealed record ProviderSpendDelta(DateOnly UsageDate, Guid? TenantId, Guid? ApiKeyId, string Provider, string ModelId, decimal Cost, int RequestCount);
