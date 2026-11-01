using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Autobahn.Integration.Tests.Support;

namespace Autobahn.Integration.Tests.Fleet;

public sealed class FleetEndpointTests
{
    private const string AdminKey = "sk-autobahn-fleet-test-admin-secret";

    [Fact]
    public async Task FleetEntryRedirectsToTheConcreteConsoleDocument()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(AdminKey);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/admin/fleet/");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/admin/fleet/index.html");
    }

    [Fact]
    public async Task FleetRequiresAnAdminSessionAndTheConsoleRequestHeader()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(AdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        using var client = factory.CreateClient();
        (await client.GetAsync("/admin/fleet/api/nodes")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/admin/fleet/api/session", new { key = AdminKey })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        client.DefaultRequestHeaders.Add("X-AutobahnFleet-Request", "1");
        var login = await client.PostAsJsonAsync("/admin/fleet/api/session", new { key = AdminKey }); login.EnsureSuccessStatusCode();
        var me = await client.GetFromJsonAsync<JsonElement>("/admin/fleet/api/me");
        me.GetProperty("canWrite").GetBoolean().Should().BeTrue();
        (await client.GetAsync("/admin/fleet/api/nodes")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync("/admin/fleet/api/session")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/admin/fleet/api/nodes")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InferenceKeysCannotSignIntoTheFleetConsole()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(AdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        using var admin = factory.CreateAdminClient(AdminKey);
        var issued = await admin.PostAsJsonAsync("/admin/api/keys", new { role = "Inference" }); issued.EnsureSuccessStatusCode();
        var key = (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString();
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-AutobahnFleet-Request", "1");
        (await client.PostAsJsonAsync("/admin/fleet/api/session", new { key })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FleetSessionCookieIsPrivateAndInvalidCookiesCannotAuthenticate()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(AdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-AutobahnFleet-Request", "1");
        var response = await client.PostAsJsonAsync("/admin/fleet/api/session", new { key = AdminKey });
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/admin/fleet");
        cookie.Should().NotContain(AdminKey);
        client.DefaultRequestHeaders.Add("Cookie", "autobahn_fleet_session=invalid-cookie");
        (await client.GetAsync("/admin/fleet/api/nodes")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/admin/fleet/api/nodes/node-a/store/delete")]
    [InlineData("/admin/fleet/api/nodes/node-a/downloads")]
    [InlineData("/admin/fleet/api/inventory/apply")]
    public async Task TenantAdminCannotMutateSharedCompute(string path)
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(AdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        using var owner = factory.CreateAdminClient(AdminKey);
        var tenantResponse = await owner.PostAsJsonAsync("/admin/api/tenants", new { slug = "fleet-readonly-team", name = "Read-only team" });
        tenantResponse.EnsureSuccessStatusCode();
        var tenantId = (await tenantResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        owner.DefaultRequestHeaders.Add("X-Autobahn-Tenant", tenantId);
        var keyResponse = await owner.PostAsJsonAsync("/admin/api/keys", new { role = "Admin", label = "fleet-viewer" });
        keyResponse.EnsureSuccessStatusCode();
        var key = (await keyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString();
        using var viewer = factory.CreateClient();
        viewer.DefaultRequestHeaders.Add("X-AutobahnFleet-Request", "1");
        (await viewer.PostAsJsonAsync("/admin/fleet/api/session", new { key })).EnsureSuccessStatusCode();
        (await viewer.GetFromJsonAsync<JsonElement>("/admin/fleet/api/me")).GetProperty("canWrite").GetBoolean().Should().BeFalse();
        (await viewer.PostAsJsonAsync(path, new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync("/admin/fleet/api/session")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await viewer.GetAsync("/admin/fleet/api/nodes")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
