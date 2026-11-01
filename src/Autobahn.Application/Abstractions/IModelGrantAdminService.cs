using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

public interface IModelGrantAdminService
{
    Task<ModelGrantsResponse> GetTenantGrantsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<ModelGrantsResponse> ReplaceTenantGrantsAsync(
        Guid tenantId,
        ReplaceModelGrantsRequest request,
        CancellationToken cancellationToken = default);

    Task<ModelGrantsResponse> GetApiKeyGrantsAsync(
        Guid tenantId,
        Guid apiKeyId,
        CancellationToken cancellationToken = default);

    Task<ModelGrantsResponse> ReplaceApiKeyGrantsAsync(
        Guid tenantId,
        Guid apiKeyId,
        ReplaceModelGrantsRequest request,
        CancellationToken cancellationToken = default);
}
