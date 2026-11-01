using Autobahn.Domain.Identity;

namespace Autobahn.Application.Abstractions;

public interface IModelGrantService
{
    Task<bool> IsModelAllowedAsync(
        Guid tenantId,
        Guid apiKeyId,
        string canonicalModelId,
        CancellationToken cancellationToken = default);

    /// <summary>The same decision with the rule that made it, for a refusal that names its cause.</summary>
    Task<ModelAccessDecision> EvaluateAsync(
        Guid tenantId,
        Guid apiKeyId,
        string canonicalModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a paid provider is open for both the tenant and the key. Asked for a standby
    /// backend, whose provider is not the one the model's own check read.
    /// </summary>
    Task<bool> IsProviderOpenAsync(
        Guid tenantId,
        Guid apiKeyId,
        string provider,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(!Autobahn.Domain.Models.ModelProviders.IsPaid(provider));

    void InvalidateTenantGrants(Guid tenantId);

    /// <summary>Drops the cached provider switches of a tenant and its keys.</summary>
    void InvalidateProviderAccess(Guid tenantId);

    void InvalidateApiKeyGrants(Guid apiKeyId);
}
