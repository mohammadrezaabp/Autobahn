using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Autobahn.Integration.Tests.Support;

namespace Autobahn.Integration.Tests.Admin;

/// <summary>
/// What a tenant's own admin key can read about itself. The console signs such a key in with
/// <c>/admin/api/session</c> and shows it <c>/admin/api/tenant/rate-limits</c>; before these two
/// existed its only sign-in check was an Operator route, so the key was refused at the door.
/// </summary>
public sealed class AdminSessionEndpointTests
{
    private const string OperatorAdminKey = "sk-Autobahn-session-endpoints-operator";

    [Fact]
    public async Task Session_ForAnOperatorKey_SaysOperator()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = CreateClient(factory, OperatorAdminKey);

        using var json = await GetJsonAsync(client, "/admin/api/session");

        json.RootElement.GetProperty("operator").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("tenantSlug").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Session_ForATenantsOwnAdminKey_AnswersWithItsTenant_AndNotOperator()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var operatorClient = CreateClient(factory, OperatorAdminKey);
        var tenantId = await CreateTenantAsync(operatorClient, "team-a", "Team A");
        var (_, secret) = await CreateKeyAsync(operatorClient, tenantId, "Admin", "team-a-admin");
        var tenantClient = CreateClient(factory, secret);

        using var json = await GetJsonAsync(tenantClient, "/admin/api/session");

        json.RootElement.GetProperty("operator").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        json.RootElement.GetProperty("tenantSlug").GetString().Should().Be("team-a");
        json.RootElement.GetProperty("tenantName").GetString().Should().Be("Team A");
        (await tenantClient.GetAsync("/admin/api/config/status")).StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the gateway-wide surface stays the operator's");
    }

    [Fact]
    public async Task Session_WithoutACredential_Is401_AndWithAnInferenceKey_Is403()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var operatorClient = CreateClient(factory, OperatorAdminKey);
        var tenantId = await CreateTenantAsync(operatorClient, "team-a", "Team A");
        var (_, inferenceSecret) = await CreateKeyAsync(operatorClient, tenantId, "Inference", "team-a-app");

        (await factory.CreateClient().GetAsync("/admin/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        foreach (var path in new[] { "/admin/api/session", "/admin/api/tenant/rate-limits" })
        {
            (await CreateClient(factory, inferenceSecret).GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        }
    }

    /// <summary>
    /// The rules of the caller's tenant and of its keys come back; another tenant's, a model's and
    /// the whole gateway's do not.
    /// </summary>
    [Fact]
    public async Task TenantRateLimits_ShowOnlyTheCallersTenant()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var operatorClient = CreateClient(factory, OperatorAdminKey);
        var teamA = await CreateTenantAsync(operatorClient, "team-a", "Team A");
        var teamB = await CreateTenantAsync(operatorClient, "team-b", "Team B");
        var (_, adminSecret) = await CreateKeyAsync(operatorClient, teamA, "Admin", "team-a-admin");
        var (keyA, _) = await CreateKeyAsync(operatorClient, teamA, "Inference", "team-a-app");
        var (keyB, _) = await CreateKeyAsync(operatorClient, teamB, "Inference", "team-b-app");

        foreach (var rule in new object[]
                 {
                     new { scope = "tenant", target = "team-a", rpm = 0, burst = 0, maxConcurrentStreams = 8, tpm = 500000 },
                     new { scope = "api_key", target = keyA.ToString(), rpm = 60, burst = 10, maxConcurrentStreams = 4 },
                     new { scope = "api_key_model", target = $"{keyA}|some-model", rpm = 6, burst = 3, maxConcurrentStreams = 2 },
                     new { scope = "tenant", target = "team-b", rpm = 30, burst = 3, maxConcurrentStreams = 2 },
                     new { scope = "api_key", target = keyB.ToString(), rpm = 10, burst = 1, maxConcurrentStreams = 1 },
                     new { scope = "model", target = "some-model", rpm = 100, burst = 10, maxConcurrentStreams = 10 },
                 })
        {
            var put = await operatorClient.PutAsJsonAsync("/admin/api/rate-limits/rules", rule);
            put.StatusCode.Should().Be(HttpStatusCode.OK, because: await put.Content.ReadAsStringAsync());
        }

        var tenantClient = CreateClient(factory, adminSecret);
        using var json = await GetJsonAsync(tenantClient, "/admin/api/tenant/rate-limits");

        json.RootElement.GetProperty("tenant").GetProperty("slug").GetString().Should().Be("team-a");
        json.RootElement.GetProperty("default").GetProperty("rpm").GetInt32().Should().BeGreaterThan(0);
        var rules = json.RootElement.GetProperty("rules").EnumerateArray()
            .Select(r => $"{r.GetProperty("scope").GetString()}:{r.GetProperty("target").GetString()}")
            .ToList();
        rules.Should().BeEquivalentTo(
            $"tenant:team-a",
            $"api_key:{keyA}",
            $"api_key_model:{keyA}|some-model");

        (await tenantClient.GetAsync("/admin/api/rate-limits")).StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the whole configuration, and every write to it, stays the operator's");
    }

    [Fact]
    public async Task TenantRateLimits_ForAnOperatorNamingATenant_AnswerForThatTenant()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(OperatorAdminKey);
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var operatorClient = CreateClient(factory, OperatorAdminKey);
        var teamA = await CreateTenantAsync(operatorClient, "team-a", "Team A");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/api/tenant/rate-limits");
        request.Headers.Add("X-Autobahn-Tenant", teamA.ToString());
        var response = await operatorClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("tenant").GetProperty("id").GetGuid().Should().Be(teamA);
        json.RootElement.GetProperty("rules").GetArrayLength().Should().Be(0);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string apiKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: body);
        return JsonDocument.Parse(body);
    }

    private static async Task<Guid> CreateTenantAsync(HttpClient client, string slug, string name)
    {
        var response = await client.PostAsJsonAsync("/admin/api/tenants", new { slug, name });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, because: body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Id, string Secret)> CreateKeyAsync(
        HttpClient operatorClient,
        Guid tenantId,
        string role,
        string label)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/api/keys")
        {
            Content = JsonContent.Create(new { role, label }),
        };
        request.Headers.Add("X-Autobahn-Tenant", tenantId.ToString());
        var response = await operatorClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, because: body);
        using var json = JsonDocument.Parse(body);
        return (json.RootElement.GetProperty("id").GetGuid(), json.RootElement.GetProperty("secret").GetString()!);
    }
}
