using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Autobahn.Integration.Tests.Support;

namespace Autobahn.Integration.Tests.Admin;

/// <summary>
/// The walk an operator takes to give one key one OpenRouter model, with the refusal a caller gets
/// at each step before it is finished.
/// </summary>
public sealed class ProviderAccessEndpointTests
{
    private const string AdminKey = "sk-Autobahn-provider-access-admin";
    private const string PaidModel = "qwen/paid-mock";
    private const string SelfHostedModel = "local-mock";

    [Fact]
    public async Task OpenRouterModel_IsRefusedWithItsReason_UntilTenantAndKeyAreOpened()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);
        await RegisterPaidModelAsync(admin);
        var (keyId, secret) = await CreateInferenceKeyAsync(admin);
        var caller = CreateClient(factory, secret);

        // On the key's list by prefix, and still closed: the list alone never opens a paid provider.
        (await admin.PutAsJsonAsync(
            $"/admin/api/keys/{keyId}/model-grants",
            new { modelIds = new[] { SelfHostedModel }, patterns = new[] { "qwen/*" } })).EnsureSuccessStatusCode();

        (await ChatAsync(caller, PaidModel)).Should().Be((HttpStatusCode.Forbidden, "provider_closed_for_tenant"));
        (await ListModelIdsAsync(caller)).Should().Equal(SelfHostedModel);
        (await ChatAsync(caller, SelfHostedModel)).Status.Should().Be(HttpStatusCode.OK);

        // A key cannot be opened ahead of its tenant.
        var early = await admin.PutAsJsonAsync($"/admin/api/keys/{keyId}/provider-access/openrouter", new { open = true });
        early.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await admin.PutAsJsonAsync("/admin/api/tenant/provider-access/openrouter", new { open = true }))
            .EnsureSuccessStatusCode();
        (await ChatAsync(caller, PaidModel)).Should().Be((HttpStatusCode.Forbidden, "provider_closed_for_key"));
        (await CheckAsync(admin, keyId, PaidModel)).Should().Be((false, "provider_closed_for_key"));

        var opened = await admin.PutAsJsonAsync($"/admin/api/keys/{keyId}/provider-access/openrouter", new { open = true });
        opened.EnsureSuccessStatusCode();
        using (var doc = JsonDocument.Parse(await opened.Content.ReadAsStringAsync()))
        {
            var openRouter = doc.RootElement.GetProperty("providers").EnumerateArray()
                .Single(p => p.GetProperty("provider").GetString() == "openrouter");
            openRouter.GetProperty("open").GetBoolean().Should().BeTrue();
            openRouter.GetProperty("tenantOpen").GetBoolean().Should().BeTrue();
        }

        (await ChatAsync(caller, PaidModel)).Status.Should().Be(HttpStatusCode.OK);
        (await ListModelIdsAsync(caller)).Should().BeEquivalentTo([SelfHostedModel, PaidModel]);
        (await CheckAsync(admin, keyId, PaidModel)).Should().Be((true, null));

        // Denying the one model takes it back without touching the prefix.
        (await admin.PutAsJsonAsync(
            $"/admin/api/keys/{keyId}/model-grants",
            new { patterns = new[] { "qwen/*" }, deniedModelIds = new[] { PaidModel } })).EnsureSuccessStatusCode();
        (await ChatAsync(caller, PaidModel)).Should().Be((HttpStatusCode.Forbidden, "model_not_on_key_list"));

        // Closing the tenant closes every key under it at once.
        (await admin.PutAsJsonAsync(
            $"/admin/api/keys/{keyId}/model-grants",
            new { patterns = new[] { "qwen/*" } })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync("/admin/api/tenant/provider-access/openrouter", new { open = false }))
            .EnsureSuccessStatusCode();
        (await ChatAsync(caller, PaidModel)).Should().Be((HttpStatusCode.Forbidden, "provider_closed_for_tenant"));
    }

    [Fact]
    public async Task SelfHostedProvider_HasNoSwitch()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);

        var response = await admin.PutAsJsonAsync("/admin/api/tenant/provider-access/self-hosted", new { open = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AdminModelList_ReportsEachModelsProvider()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);
        await RegisterPaidModelAsync(admin);

        using var doc = JsonDocument.Parse(await admin.GetStringAsync("/admin/api/models"));
        var providers = doc.RootElement.EnumerateArray().ToDictionary(
            m => m.GetProperty("model").GetProperty("id").GetString()!,
            m => m.GetProperty("provider").GetString());

        providers[PaidModel].Should().Be("openrouter");
        providers[SelfHostedModel].Should().Be("self-hosted");
    }

    /// <summary>
    /// Registered against the mock upstream with the provider set by hand, which is the path an
    /// operator takes to correct a model the address does not give away.
    /// </summary>
    private static async Task RegisterPaidModelAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/admin/api/models", new
        {
            model = new
            {
                id = PaidModel,
                url = "http://127.0.0.1:18080",
                maxContextLength = 8192,
                provider = "openrouter",
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<(Guid KeyId, string Secret)> CreateInferenceKeyAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/admin/api/keys", new { role = "Inference" });
        response.EnsureSuccessStatusCode();
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (created.RootElement.GetProperty("id").GetGuid(), created.RootElement.GetProperty("secret").GetString()!);
    }

    private static async Task<(HttpStatusCode Status, string? Code)> ChatAsync(HttpClient caller, string model)
    {
        using var body = new StringContent(
            $$"""{"model":"{{model}}","stream":false}""",
            System.Text.Encoding.UTF8,
            "application/json");
        var response = await caller.PostAsync("/v1/chat/completions", body);
        var code = response.Headers.TryGetValues("X-Autobahn-Error-Code", out var values) ? values.Single() : null;
        return (response.StatusCode, code);
    }

    private static async Task<List<string?>> ListModelIdsAsync(HttpClient caller)
    {
        using var doc = JsonDocument.Parse(await caller.GetStringAsync("/v1/models"));
        return doc.RootElement.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
    }

    private static async Task<(bool Allowed, string? Reason)> CheckAsync(HttpClient admin, Guid keyId, string model)
    {
        var json = await admin.GetStringAsync(
            $"/admin/api/access/check?keyId={keyId}&model={Uri.EscapeDataString(model)}");
        using var doc = JsonDocument.Parse(json);
        return (
            doc.RootElement.GetProperty("allowed").GetBoolean(),
            doc.RootElement.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
                ? reason.GetString()
                : null);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var configPath = IntegrationModelsConfig.WriteStandardModelsConfig();
        return GatewayWebApplicationFactory.CreateWithInMemoryDatabase(
            AdminKey,
            upstreamHandler: new MockUpstreamHandler(),
            configureSettings: settings =>
                IntegrationModelsConfig.ApplyStandardModelsSettings(settings, configPath));
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string apiKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }
}
