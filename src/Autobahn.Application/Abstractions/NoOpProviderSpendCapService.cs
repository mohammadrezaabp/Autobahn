namespace Autobahn.Application.Abstractions;
/// <summary>Stands in where there is no database: nothing is capped and nothing is spent.</summary>
public sealed class NoOpProviderSpendCapService : IProviderSpendCapService
{
    public ValueTask<ProviderSpendDecision> TryReserveAsync(Guid tenantId, Guid apiKeyId, string provider, string requestId, string canonicalModelId, long? requestedMaxTokens, long requestBodyBytes, CancellationToken cancellationToken = default) => ValueTask.FromResult(ProviderSpendDecision.Allowed);
    public void Release(string requestId)
    {
    }

    public void Invalidate(Guid tenantId, string provider)
    {
    }

    public Task<ProviderMonthSpend> GetMonthSpendAsync(Guid tenantId, string provider, CancellationToken cancellationToken = default) => Task.FromResult(ProviderMonthSpend.Empty);
}
