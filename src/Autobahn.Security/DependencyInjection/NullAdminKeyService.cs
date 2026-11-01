using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;
using Autobahn.Application.Security;
using Autobahn.Persistence.DependencyInjection;
using Autobahn.Security.Authentication;
using Autobahn.Security.Authorization;
using Autobahn.Security.Configuration;
using Autobahn.Security.Hosting;
using Autobahn.Security.Audit;
using Autobahn.Security.Services;

namespace Autobahn.Security.DependencyInjection;
internal sealed class NullAdminKeyService : IAdminKeyService
{
    private static InvalidOperationException NotConfigured() => new("API key administration requires ConnectionStrings:GatewayDb.");
    public Task<AdminApiKeyCreatedResponse> CreateAsync(Guid tenantId, CreateAdminApiKeyRequest request, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<IReadOnlyList<AdminApiKeyListItem>> ListAsync(Guid tenantId, bool includeUsageSummary = false, bool includeArchived = false, Guid? actorKeyId = null, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<AdminApiKeyListItem> UpdateAsync(Guid tenantId, Guid keyId, UpdateAdminApiKeyRequest request, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<AdminApiKeyUsageResponse> GetUsageAsync(Guid tenantId, Guid keyId, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task RevokeAsync(Guid tenantId, Guid keyId, Guid? actorKeyId = null, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<int> RevokeManyAsync(Guid tenantId, IReadOnlyCollection<Guid> keyIds, Guid? actorKeyId = null, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task ArchiveAsync(Guid tenantId, Guid keyId, Guid? actorKeyId = null, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task UnarchiveAsync(Guid tenantId, Guid keyId, Guid? actorKeyId = null, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<AdminApiKeyListItem> DeleteAsync(Guid tenantId, Guid keyId, Guid? actorKeyId, string? confirmKeyPrefix, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<AdminApiKeyLifecycleResponse> GetLifecycleAsync(Guid tenantId, Guid keyId, CancellationToken cancellationToken = default) => throw NotConfigured();
}
