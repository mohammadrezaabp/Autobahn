using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

public interface IProviderAccessAdminService
{
    Task<ProviderAccessResponse> GetTenantAccessAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<ProviderAccessChange> SetTenantAccessAsync(
        Guid tenantId,
        string provider,
        bool open,
        CancellationToken cancellationToken = default);

    Task<ProviderAccessResponse> GetApiKeyAccessAsync(
        Guid tenantId,
        Guid apiKeyId,
        CancellationToken cancellationToken = default);

    Task<ProviderAccessChange> SetApiKeyAccessAsync(
        Guid tenantId,
        Guid apiKeyId,
        string provider,
        bool open,
        CancellationToken cancellationToken = default);

    /// <summary>Sets or removes (null) the tenant's monthly cap at a provider.</summary>
    Task<ProviderCapChange> SetTenantCapAsync(
        Guid tenantId,
        string provider,
        decimal? monthlyCapUsd,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Provider access administration requires ConnectionStrings:GatewayDb.");

    /// <summary>Sets or removes (null) a key's monthly cap at a provider. It cannot exceed the tenant's.</summary>
    Task<ProviderCapChange> SetApiKeyCapAsync(
        Guid tenantId,
        Guid apiKeyId,
        string provider,
        decimal? monthlyCapUsd,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Provider access administration requires ConnectionStrings:GatewayDb.");

    /// <summary>What the gateway would answer if this key asked for this model now, and why.</summary>
    Task<AccessCheckResponse> CheckAsync(
        Guid tenantId,
        Guid apiKeyId,
        string model,
        CancellationToken cancellationToken = default);
}
