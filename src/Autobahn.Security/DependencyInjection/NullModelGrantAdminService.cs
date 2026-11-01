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
internal sealed class NullModelGrantAdminService : IModelGrantAdminService
{
    private static InvalidOperationException NotConfigured() => new("Model grant administration requires ConnectionStrings:GatewayDb.");
    public Task<ModelGrantsResponse> GetTenantGrantsAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<ModelGrantsResponse> ReplaceTenantGrantsAsync(Guid tenantId, ReplaceModelGrantsRequest request, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<ModelGrantsResponse> GetApiKeyGrantsAsync(Guid tenantId, Guid apiKeyId, CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<ModelGrantsResponse> ReplaceApiKeyGrantsAsync(Guid tenantId, Guid apiKeyId, ReplaceModelGrantsRequest request, CancellationToken cancellationToken = default) => throw NotConfigured();
}
