namespace Autobahn.Application.Abstractions;
/// <summary>
/// The monthly spend caps on paid providers, per key and per tenant.
/// </summary>
/// <remarks>
/// A request reserves its estimated cost before it is forwarded, as hard-stop budgets do, so
/// requests in flight together cannot each see the full headroom and overshoot the cap between
/// them. The reservation is released once the request's real cost has reached the spend table, or
/// at once when the request produced no usage.
/// </remarks>
public interface IProviderSpendCapService
{
    ValueTask<ProviderSpendDecision> TryReserveAsync(Guid tenantId, Guid apiKeyId, string provider, string requestId, string canonicalModelId, long? requestedMaxTokens, long requestBodyBytes, CancellationToken cancellationToken = default);
    /// <summary>Releases a reservation (no-op if unknown).</summary>
    void Release(string requestId);
    /// <summary>
    /// Discards the cached spend and caps of a tenant at a provider: after spend was written, and
    /// after a cap or a switch changed.
    /// </summary>
    void Invalidate(Guid tenantId, string provider);
    /// <summary>This month's persisted spend, read from the database.</summary>
    Task<ProviderMonthSpend> GetMonthSpendAsync(Guid tenantId, string provider, CancellationToken cancellationToken = default);
}
