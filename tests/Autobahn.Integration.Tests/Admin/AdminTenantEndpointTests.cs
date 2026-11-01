using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Autobahn.Domain.Identity;
using Autobahn.Integration.Tests.Support;
using Autobahn.Persistence;
using Autobahn.Persistence.Entities;
using Autobahn.Persistence.Security;

namespace Autobahn.Integration.Tests.Admin;

/// <summary>
/// Tenant create, update and key move. These act across tenants, so they sit behind the Operator
/// policy rather than the per-tenant Admin role the key endpoints use.
/// </summary>
public sealed class AdminTenantEndpointTests
{
    private const string OperatorAdminKey = "sk-Autobahn-tenant-endpoints-operator";
    private const string TenantBAdminKey = "sk-Autobahn-tenant-endpoints-b-admin";
    private const string IntegrationPepper = "integration-test-pepper";

    [Fact]
    public async Task CreateTenant_ThenList_ShowsItBesideTheOperatorTenant()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = CreateClient(factory, OperatorAdminKey);

        var create = await client.PostAsJsonAsync(
            "/admin/api/tenants",
            new { slug = "Fanus-MMT", name = "Fanus MMT", costCenter = "fanus" });

        var createBody = await create.Content.ReadAsStringAsync();
        create.StatusCode.Should().Be(HttpStatusCode.Created, because: createBody);
        using var created = JsonDocument.Parse(createBody);
        created.RootElement.GetProperty("slug").GetString().Should().Be("fanus-mmt");
        created.RootElement.GetProperty("isOperatorTenant").GetBoolean().Should().BeFalse();

        var list = await client.GetAsync("/admin/api/tenants");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using var listed = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var tenants = listed.RootElement.EnumerateArray().ToList();
        tenants.Should().HaveCount(2);
        tenants.Should().ContainSingle(t => t.GetProperty("isOperatorTenant").GetBoolean());
        var fanus = tenants.Single(t => t.GetProperty("slug").GetString() == "fanus-mmt");
        fanus.GetProperty("keyCount").GetInt32().Should().Be(0);
        fanus.GetProperty("costCenter").GetString().Should().Be("fanus");
    }

    [Fact]
    public async Task CreateTenant_TakenSlug_Is409WithACode()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = CreateClient(factory, OperatorAdminKey);
        (await client.PostAsJsonAsync("/admin/api/tenants", new { slug = "team-a", name = "Team A" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var again = await client.PostAsJsonAsync("/admin/api/tenants", new { slug = "team-a", name = "Other" });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var json = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().Should().Be("tenant_slug_taken");
    }

    [Theory]
    [InlineData("""{"slug":"has space","name":"X"}""")]
    [InlineData("""{"slug":"team-a"}""")]
    [InlineData("""{"slug":"team-a","name":"X","planSlug":"no-such-plan"}""")]
    public async Task CreateTenant_InvalidRequest_Is400(string body)
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = CreateClient(factory, OperatorAdminKey);

        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/admin/api/tenants", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because: await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UpdateTenant_ReplacesEditableFields()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = CreateClient(factory, OperatorAdminKey);
        var tenantId = await CreateTenantAsync(client, "team-a");

        var update = await client.PutAsJsonAsync(
            $"/admin/api/tenants/{tenantId}",
            new { name = "Team Alpha", costCenter = "alpha" });

        update.StatusCode.Should().Be(HttpStatusCode.OK, because: await update.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("slug").GetString().Should().Be("team-a");
        json.RootElement.GetProperty("name").GetString().Should().Be("Team Alpha");
        json.RootElement.GetProperty("costCenter").GetString().Should().Be("alpha");
    }

    [Fact]
    public async Task UpdateTenant_UnknownId_Is404()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = CreateClient(factory, OperatorAdminKey);

        var update = await client.PutAsJsonAsync($"/admin/api/tenants/{Guid.NewGuid()}", new { name = "X" });

        update.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The point of the slice: a key issued in the shared tenant is handed to a team's own tenant
    /// and keeps working, now as that tenant's key.
    /// </summary>
    [Fact]
    public async Task MoveKey_ReassignsIt_AndTheSecretAuthenticatesAsTheNewTenant()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = CreateClient(factory, OperatorAdminKey);
        var tenantId = await CreateTenantAsync(client, "team-a");

        // An admin key, so that listing keys with it shows which tenant it now acts for. A second
        // admin key (the operator's) stays behind, so the last-admin guard does not hold it back.
        var createKey = await client.PostAsJsonAsync("/admin/api/keys", new { role = "Admin", label = "team-a-admin" });
        createKey.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdKey = JsonDocument.Parse(await createKey.Content.ReadAsStringAsync());
        var keyId = createdKey.RootElement.GetProperty("id").GetGuid();
        var secret = createdKey.RootElement.GetProperty("secret").GetString()!;

        // Used once before the move, so its validation is cached under the old tenant.
        var movedClient = CreateClient(factory, secret);
        (await movedClient.GetAsync("/admin/api/tenants")).StatusCode.Should().Be(
            HttpStatusCode.OK,
            "an admin key of the operator tenant holds the Operator policy");

        var move = await client.PostAsJsonAsync(
            $"/admin/api/tenants/{tenantId}/keys/move",
            new { keyIds = new[] { keyId } });

        var moveBody = await move.Content.ReadAsStringAsync();
        move.StatusCode.Should().Be(HttpStatusCode.OK, because: moveBody);
        using var moved = JsonDocument.Parse(moveBody);
        moved.RootElement.GetProperty("tenantSlug").GetString().Should().Be("team-a");
        moved.RootElement.GetProperty("moved").GetArrayLength().Should().Be(1);
        moved.RootElement.GetProperty("skipped").GetArrayLength().Should().Be(0);

        // Same secret, new tenant: its own key list is now team-a's, and it has left the operator
        // tenant, so the gateway-wide surface is closed to it.
        var ownKeys = await movedClient.GetAsync("/admin/api/keys");
        ownKeys.StatusCode.Should().Be(HttpStatusCode.OK);
        using var ownKeysJson = JsonDocument.Parse(await ownKeys.Content.ReadAsStringAsync());
        ownKeysJson.RootElement.EnumerateArray().Select(k => k.GetProperty("id").GetGuid()).Should().Equal(keyId);
        (await movedClient.GetAsync("/admin/api/tenants")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await ListKeyIdsAsync(client)).Should().NotContain(keyId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var events = await db.ApiKeyLifecycleEvents
            .Where(e => e.ApiKeyId == keyId)
            .Select(e => new { e.Event, e.TenantId })
            .ToListAsync();
        events.Should().Contain(e => e.Event == ApiKeyLifecycleEvent.MovedIn && e.TenantId == tenantId);
        events.Should().Contain(e => e.Event == ApiKeyLifecycleEvent.MovedOut && e.TenantId != tenantId);
    }

    [Fact]
    public async Task MoveKey_TheCallersOwnKey_IsSkippedWithAReason()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = CreateClient(factory, OperatorAdminKey);
        var tenantId = await CreateTenantAsync(client, "team-a");
        var ownKeyId = (await ListKeyIdsAsync(client)).Single();

        var move = await client.PostAsJsonAsync(
            $"/admin/api/tenants/{tenantId}/keys/move",
            new { keyIds = new[] { ownKeyId } });

        move.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await move.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("moved").GetArrayLength().Should().Be(0);
        json.RootElement.GetProperty("skipped")[0].GetProperty("code").GetString().Should().Be("self_action");
        (await client.GetAsync("/admin/api/tenants")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MoveKey_UnknownTenant_Is404_AndNoIds_Is400()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = CreateClient(factory, OperatorAdminKey);
        var tenantId = await CreateTenantAsync(client, "team-a");

        var unknown = await client.PostAsJsonAsync(
            $"/admin/api/tenants/{Guid.NewGuid()}/keys/move",
            new { keyIds = new[] { Guid.NewGuid() } });
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var empty = await client.PostAsJsonAsync(
            $"/admin/api/tenants/{tenantId}/keys/move",
            new { keyIds = Array.Empty<Guid>() });
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A tenant's own admin must not be able to create tenants or pull another tenant's keys into
    /// its own.
    /// </summary>
    [Fact]
    public async Task TenantEndpoints_ForATenantAdminOutsideTheOperatorTenant_AreForbidden()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        var tenantBId = await SeedSecondTenantAdminAsync(factory);
        var operatorClient = CreateClient(factory, OperatorAdminKey);
        var operatorKeyId = (await ListKeyIdsAsync(operatorClient)).Single();

        var client = CreateClient(factory, TenantBAdminKey);

        (await client.GetAsync("/admin/api/tenants")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/admin/api/tenants", new { slug = "mine", name = "Mine" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync($"/admin/api/tenants/{tenantBId}", new { name = "Renamed" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync(
                $"/admin/api/tenants/{tenantBId}/keys/move",
                new { keyIds = new[] { operatorKeyId } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await ListKeyIdsAsync(operatorClient)).Should().Contain(operatorKeyId);
    }

    [Fact]
    public async Task TenantEndpoints_WithoutACredential_Are401()
    {
        using var factory = GatewayWebApplicationFactory.Create();
        using var client = factory.CreateClient();

        (await client.GetAsync("/admin/api/tenants")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/admin/api/tenants", new { slug = "x", name = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Once keys are sorted into a tenant per team, the operator still has to see and manage them.
    /// The key endpoints stay scoped to one tenant; the operator names which with a header.
    /// </summary>
    [Fact]
    public async Task TenantHeader_FromTheOperator_ScopesKeyEndpointsToThatTenant()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        var tenantBId = await SeedSecondTenantAdminAsync(factory);
        var operatorClient = CreateClient(factory, OperatorAdminKey);
        var tenantBClient = CreateClient(factory, TenantBAdminKey);
        var ownKeyIds = await ListKeyIdsAsync(operatorClient);
        var tenantBKeyIds = await ListKeyIdsAsync(tenantBClient);

        var asTenantB = CreateClient(factory, OperatorAdminKey);
        asTenantB.DefaultRequestHeaders.Add("X-Autobahn-Tenant", tenantBId.ToString());

        (await ListKeyIdsAsync(asTenantB)).Should().BeEquivalentTo(tenantBKeyIds);
        (await ListKeyIdsAsync(operatorClient)).Should().BeEquivalentTo(ownKeyIds);

        var create = await asTenantB.PostAsJsonAsync("/admin/api/keys", new { role = "Inference", label = "made-by-operator" });
        var createBody = await create.Content.ReadAsStringAsync();
        create.StatusCode.Should().Be(HttpStatusCode.Created, because: createBody);
        using var created = JsonDocument.Parse(createBody);
        var createdId = created.RootElement.GetProperty("id").GetGuid();

        (await ListKeyIdsAsync(tenantBClient)).Should().Contain(createdId);
        (await ListKeyIdsAsync(operatorClient)).Should().NotContain(createdId);

        (await asTenantB.PostAsync($"/admin/api/keys/{createdId}/revoke", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        // Without the header the same key is outside the operator's own tenant.
        (await operatorClient.PostAsync($"/admin/api/keys/{createdId}/archive", content: null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantHeader_FromATenantAdmin_IsRefused_NotIgnored()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await SeedSecondTenantAdminAsync(factory);
        var operatorTenantId = await OperatorTenantIdAsync(CreateClient(factory, OperatorAdminKey));

        var client = CreateClient(factory, TenantBAdminKey);
        client.DefaultRequestHeaders.Add("X-Autobahn-Tenant", operatorTenantId.ToString());

        var response = await client.GetAsync("/admin/api/keys");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().Should().Be("operator_required");
    }

    [Theory]
    [InlineData("not-a-guid", HttpStatusCode.BadRequest, "invalid_tenant_header")]
    [InlineData("11111111-2222-3333-4444-555555555555", HttpStatusCode.NotFound, "tenant_not_found")]
    public async Task TenantHeader_ThatNamesNoTenant_IsRefused(string header, HttpStatusCode status, string code)
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = CreateClient(factory, OperatorAdminKey);
        client.DefaultRequestHeaders.Add("X-Autobahn-Tenant", header);

        var response = await client.GetAsync("/admin/api/keys");

        response.StatusCode.Should().Be(status);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    private static async Task<Guid> OperatorTenantIdAsync(HttpClient operatorClient)
    {
        using var json = JsonDocument.Parse(await operatorClient.GetStringAsync("/admin/api/tenants"));
        return json.RootElement.EnumerateArray()
            .Single(t => t.GetProperty("isOperatorTenant").GetBoolean())
            .GetProperty("id").GetGuid();
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string apiKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static async Task<Guid> CreateTenantAsync(HttpClient client, string slug)
    {
        var response = await client.PostAsJsonAsync("/admin/api/tenants", new { slug, name = slug });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, because: body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ListKeyIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/admin/api/keys");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(k => k.GetProperty("id").GetGuid()).ToList();
    }

    private static async Task<Guid> SeedSecondTenantAdminAsync(WebApplicationFactory<Program> factory)
    {
        // Starting the host runs the bootstrap seeder first, so the operator tenant exists before
        // tenant B is added beside it.
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);

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
        return tenantId;
    }
}
