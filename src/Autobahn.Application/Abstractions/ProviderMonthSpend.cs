namespace Autobahn.Application.Abstractions;
/// <summary>What a tenant and its keys have spent at one provider in the current calendar month (UTC).</summary>
public sealed record ProviderMonthSpend(decimal TenantTotal, IReadOnlyDictionary<Guid, decimal> ByApiKey)
{
    public static ProviderMonthSpend Empty { get; } = new(0m, new Dictionary<Guid, decimal>());

    public decimal ForKey(Guid apiKeyId) => ByApiKey.TryGetValue(apiKeyId, out var spent) ? spent : 0m;
}
