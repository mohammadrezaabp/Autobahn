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
internal sealed class NullProviderAccessAdminService : IProviderAccessAdminService
{
    private static InvalidOperationException NotConfigured() => new("Provider access administration requires ConnectionStrings:GatewayDb.");
    public Task<ProviderAccessResponse> GetTenantAccessAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<ProviderAccessChange> SetTenantAccessAsync(Guid tenantId, string provider, bool open, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<ProviderAccessResponse> GetApiKeyAccessAsync(Guid tenantId, Guid apiKeyId, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<ProviderAccessChange> SetApiKeyAccessAsync(Guid tenantId, Guid apiKeyId, string provider, bool open, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<AccessCheckResponse> CheckAsync(Guid tenantId, Guid apiKeyId, string model, CancellationToken cancellationToken = default) => throw NotConfigured();
}
