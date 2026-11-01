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
internal sealed class NullTenantAdminService : ITenantAdminService
{
    private static InvalidOperationException NotConfigured() => new("Tenant administration requires ConnectionStrings:GatewayDb.");
    public Task<IReadOnlyList<AdminTenantListItem>> ListAsync(CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<AdminTenantListItem> CreateAsync(CreateAdminTenantRequest request, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<AdminTenantListItem> UpdateAsync(Guid tenantId, UpdateAdminTenantRequest request, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<MoveAdminApiKeysResponse> MoveKeysAsync(Guid targetTenantId, IReadOnlyCollection<Guid> keyIds, Guid? actorKeyId = null, CancellationToken cancellationToken = default) => throw NotConfigured();
}
