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
internal sealed class NullModelGrantService : IModelGrantService
{
    public Task<bool> IsModelAllowedAsync(Guid tenantId, Guid apiKeyId, string canonicalModelId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<ModelAccessDecision> EvaluateAsync(Guid tenantId, Guid apiKeyId, string canonicalModelId, CancellationToken cancellationToken = default) => Task.FromResult(ModelAccessDecision.Allowed());
    public void InvalidateTenantGrants(Guid tenantId)
    {
    }

    public void InvalidateProviderAccess(Guid tenantId)
    {
    }

    public void InvalidateApiKeyGrants(Guid apiKeyId)
    {
    }
}
