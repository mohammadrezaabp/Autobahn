namespace Autobahn.Domain.Billing;
/// <summary>
/// What one key spent at one paid provider on one model in one day. The table behind the monthly
/// caps and the Spend tab; a day's rows are summed for a month's spend.
/// </summary>
/// <param name = "TenantId">Null for keyless traffic to a public model.</param>
public sealed record ProviderSpendRecord(DateOnly UsageDate, Guid? TenantId, Guid? ApiKeyId, string Provider, string ModelId, decimal Cost, int RequestCount);
