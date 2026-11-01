using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Autobahn.Domain.Identity;
using Autobahn.Integration.Tests.Support;
using Autobahn.Persistence;
using Autobahn.Persistence.Entities;
using Autobahn.Persistence.Security;

namespace Autobahn.Integration.Tests.Admin;

/// <summary>
/// The Admin role is per-tenant, and any tenant's admin can mint further admin keys for its own
/// tenant. The gateway-wide control plane (model registry, upstream credentials, CORS, rate limits,
/// config, backups, cross-tenant request/log feeds) therefore requires the operator tenant on top
/// of the role — otherwise every tenant's admin held the whole gateway.
/// </summary>
public sealed class OperatorTenantScopeTests
{
    private const string OperatorAdminKey = "sk-Autobahn-operator-scope-admin";
    private const string TenantBAdminKey = "sk-Autobahn-tenant-b-admin-key-1";
    private const string IntegrationPepper = "integration-test-pepper";

    [Fact]
    public async Task ControlPlane_TenantBAdminKey_IsForbidden()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await SeedSecondTenantAdminAsync(factory);

        var client = CreateClient(factory, TenantBAdminKey);

        (await client.GetAsync("/admin/api/summary")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/admin/api/requests")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/stats")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var addModel = await client.PostAsJsonAsync("/admin/api/models", new
        {
            model = new { id = "intruder-model", url = "http://attacker:9999" },
        });
        addModel.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var putCors = await client.PutAsJsonAsync("/admin/api/cors", new
        {
            allowedOrigins = new[] { "https://attacker.example" },
        });
        putCors.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Tenant scoping must not lock a tenant admin out of its own key management.</summary>
    [Fact]
    public async Task OwnTenantKeyManagement_TenantBAdminKey_StillWorks()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await SeedSecondTenantAdminAsync(factory);

        var client = CreateClient(factory, TenantBAdminKey);

        var list = await client.GetAsync("/admin/api/keys");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        var create = await client.PostAsJsonAsync("/admin/api/keys", new { role = "Inference" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ControlPlane_OperatorAdminKey_StillWorks()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await SeedSecondTenantAdminAsync(factory);

        var client = CreateClient(factory, OperatorAdminKey);

        (await client.GetAsync("/admin/api/summary")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/admin/api/models")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string apiKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static async Task SeedSecondTenantAdminAsync(WebApplicationFactory<Program> factory)
    {
        // Starting the host runs the bootstrap seeder first, so the operator tenant exists before
        // tenant B is added beside it.
        _ = factory.Server;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var now = DateTimeOffset.UtcNow;
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantEntity
        {
            Id = tenantId,
            Slug = "tenant-b",
            Name = "Tenant B",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.ApiKeys.Add(new ApiKeyEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            KeyHash = ApiKeyHashing.Hash(TenantBAdminKey, IntegrationPepper),
            KeyPrefix = ApiKeyHashing.CreatePrefix(TenantBAdminKey),
            Role = ApiKeyRole.Admin,
            Scopes = ["admin"],
            CreatedAt = now,
        });

        await db.SaveChangesAsync();
    }
}
