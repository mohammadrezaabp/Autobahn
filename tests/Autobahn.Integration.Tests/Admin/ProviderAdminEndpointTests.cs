using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Autobahn.Integration.Tests.Support;

namespace Autobahn.Integration.Tests.Admin;

/// <summary>
/// The Providers page's API, and the walk a request takes through cost and caps: an opened key
/// spends, the spend is recorded for its provider, and the key stops at its cap.
/// </summary>
public sealed class ProviderAdminEndpointTests
{
    private const string AdminKey = "sk-Autobahn-provider-admin-admin";
    private const string PaidModel = "qwen/capped-mock";

    [Fact]
    public async Task List_NamesEveryPaidProvider_AndWhatTheGatewayCanReadFromEach()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);

        using var doc = JsonDocument.Parse(await admin.GetStringAsync("/admin/api/providers"));
        var providers = doc.RootElement.GetProperty("providers").EnumerateArray()
            .ToDictionary(p => p.GetProperty("provider").GetString()!);

        providers.Keys.Should().Equal("openrouter", "openai", "deepseek", "deepgram", "elevenlabs");
        providers["openrouter"].GetProperty("reportsCost").GetBoolean().Should().BeTrue();
        providers["openrouter"].GetProperty("supportsBalance").GetBoolean().Should().BeTrue();
        // OpenAI has no balance, but its spend is read with an admin key of its own.
        providers["openai"].GetProperty("supportsBalance").GetBoolean().Should().BeTrue();
        providers["openai"].GetProperty("balanceEnvVar").GetString().Should().Be("OPENAI_ADMIN_KEY");
        providers["deepgram"].GetProperty("supportsBalance").GetBoolean().Should().BeTrue();
        providers["elevenlabs"].GetProperty("supportsBalance").GetBoolean().Should().BeTrue();
        providers["deepseek"].GetProperty("supportsCatalog").GetBoolean().Should().BeTrue();
        providers["deepgram"].GetProperty("forwardable").GetBoolean().Should().BeFalse();
        providers["elevenlabs"].GetProperty("forwardable").GetBoolean().Should().BeFalse();
        providers["openrouter"].GetProperty("openTenantCount").GetInt32().Should().Be(0, "a provider starts closed to everyone");
    }

    [Fact]
    public async Task UnknownProvider_Is404()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);

        (await admin.GetAsync("/admin/api/providers/together/access")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Settings_AreStoredPerProvider_AndValidated()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);

        var saved = await admin.PutAsJsonAsync(
            "/admin/api/providers/deepseek/settings",
            new { lowBalanceUsd = 12.5m, newModelsStartStopped = true });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        using var doc = JsonDocument.Parse(await admin.GetStringAsync("/admin/api/providers"));
        var deepSeek = doc.RootElement.GetProperty("providers").EnumerateArray()
            .Single(p => p.GetProperty("provider").GetString() == "deepseek");
        deepSeek.GetProperty("lowBalanceUsd").GetDecimal().Should().Be(12.5m);
        deepSeek.GetProperty("newModelsStartStopped").GetBoolean().Should().BeTrue();

        (await admin.PutAsJsonAsync("/admin/api/providers/deepseek/settings", new { lowBalanceUsd = -1 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>With no company key there is no catalogue to read, and the answer says what to set.</summary>
    [Fact]
    public async Task Catalog_WithoutACompanyKey_SaysWhichVariableToSet()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);

        var response = await admin.GetAsync("/admin/api/providers/openai/catalog?q=gpt");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("OPENAI_API_KEY");

        (await admin.GetAsync("/admin/api/providers/deepgram/catalog")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AKeysCap_CannotExceedItsTenants()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);
        var (keyId, _) = await CreateInferenceKeyAsync(admin);
        var tenantId = await OwnTenantIdAsync(admin);

        var opened = await admin.PutAsJsonAsync(
            $"/admin/api/providers/openrouter/access/{tenantId}",
            new { open = true, monthlyCapUsd = 10 });
        opened.StatusCode.Should().Be(HttpStatusCode.OK, await opened.Content.ReadAsStringAsync());
        using (var doc = JsonDocument.Parse(await opened.Content.ReadAsStringAsync()))
        {
            var row = doc.RootElement.GetProperty("tenants").EnumerateArray()
                .Single(t => t.GetProperty("tenantId").GetGuid() == tenantId);
            row.GetProperty("open").GetBoolean().Should().BeTrue();
            row.GetProperty("monthlyCapUsd").GetDecimal().Should().Be(10m);
        }

        var tooHigh = await admin.PutAsJsonAsync(
            $"/admin/api/keys/{keyId}/provider-access/openrouter",
            new { open = true, monthlyCapUsd = 25 });
        tooHigh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooHigh.Content.ReadAsStringAsync()).Should().Contain("cannot exceed");

        var fits = await admin.PutAsJsonAsync(
            $"/admin/api/keys/{keyId}/provider-access/openrouter",
            new { open = true, monthlyCapUsd = 4 });
        fits.StatusCode.Should().Be(HttpStatusCode.OK, await fits.Content.ReadAsStringAsync());
        using (var doc = JsonDocument.Parse(await fits.Content.ReadAsStringAsync()))
        {
            var item = doc.RootElement.GetProperty("providers").EnumerateArray()
                .Single(p => p.GetProperty("provider").GetString() == "openrouter");
            item.GetProperty("monthlyCapUsd").GetDecimal().Should().Be(4m);
            item.GetProperty("tenantMonthlyCapUsd").GetDecimal().Should().Be(10m);
        }

        // Leaving the cap out keeps it; clearCap removes it.
        (await admin.PutAsJsonAsync($"/admin/api/keys/{keyId}/provider-access/openrouter", new { open = true }))
            .EnsureSuccessStatusCode();
        (await KeyCapAsync(admin, keyId)).Should().Be(4m);
        (await admin.PutAsJsonAsync($"/admin/api/keys/{keyId}/provider-access/openrouter", new { open = true, clearCap = true }))
            .EnsureSuccessStatusCode();
        (await KeyCapAsync(admin, keyId)).Should().BeNull();
    }

    /// <summary>
    /// The model costs 1 USD a token, the mock upstream answers 18 tokens, and the request asks
    /// for at most 10: about 20 USD is reserved each time. Under a 30 USD cap the first request
    /// fits and the second does not, whether or not the first one's cost has been written yet.
    /// </summary>
    [Fact]
    public async Task AnOpenedKey_Spends_IsCounted_AndStopsAtItsCap()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);
        await RegisterPaidModelAsync(admin);
        var (keyId, secret) = await CreateInferenceKeyAsync(admin);
        var caller = CreateClient(factory, secret);
        var tenantId = await OwnTenantIdAsync(admin);

        (await admin.PutAsJsonAsync($"/admin/api/keys/{keyId}/model-grants", new { patterns = new[] { "openrouter:*" } }))
            .EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/admin/api/providers/openrouter/access/{tenantId}", new { open = true }))
            .EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync(
            $"/admin/api/keys/{keyId}/provider-access/openrouter",
            new { open = true, monthlyCapUsd = 30 })).EnsureSuccessStatusCode();

        var first = await ChatAsync(caller);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        first.Headers.GetValues("X-Autobahn-Provider").Single().Should().Be("openrouter");

        var second = await ChatAsync(caller);
        second.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        second.Headers.GetValues("X-Autobahn-Error-Code").Single().Should().Be("spend_cap_reached");
        (await second.Content.ReadAsStringAsync()).Should().Contain("monthly OpenRouter cap of 30 USD");

        // The first request's cost reaches the provider's spend once the usage writer has flushed.
        JsonElement report = default;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var doc = JsonDocument.Parse(await admin.GetStringAsync("/admin/api/providers/openrouter/spend?days=7"));
            report = doc.RootElement.Clone();
            if (report.GetProperty("requests").GetInt32() > 0)
            {
                break;
            }

            await Task.Delay(100);
        }

        report.GetProperty("totalUsd").GetDecimal().Should().Be(18m, "12 prompt and 6 completion tokens at 1 USD each");
        report.GetProperty("requests").GetInt32().Should().Be(1);
        report.GetProperty("daily").GetArrayLength().Should().Be(7, "a day with nothing spent is a zero, not a gap");
        report.GetProperty("byModel")[0].GetProperty("id").GetString().Should().Be(PaidModel);
        report.GetProperty("byKey")[0].GetProperty("id").GetGuid().Should().Be(keyId);

        // And the key's own page says what it has spent against its cap.
        using var access = JsonDocument.Parse(await admin.GetStringAsync($"/admin/api/keys/{keyId}/provider-access"));
        access.RootElement.GetProperty("providers").EnumerateArray()
            .Single(p => p.GetProperty("provider").GetString() == "openrouter")
            .GetProperty("spentThisMonthUsd").GetDecimal().Should().Be(18m);

        // Raising the cap lets the key through again.
        (await admin.PutAsJsonAsync(
            $"/admin/api/keys/{keyId}/provider-access/openrouter",
            new { open = true, monthlyCapUsd = 100 })).EnsureSuccessStatusCode();
        (await ChatAsync(caller)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A request priced at 1 USD a token, the price then halved: a dry run says what the stored
    /// usage would cost and changes nothing; applying it moves the provider spend behind the caps
    /// and the usage report with it.
    /// </summary>
    [Fact]
    public async Task Reprice_DryRunReports_ApplyRewritesTheSpend()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);
        await RegisterPaidModelAsync(admin);
        var (keyId, secret) = await CreateInferenceKeyAsync(admin);
        var caller = CreateClient(factory, secret);
        var tenantId = await OwnTenantIdAsync(admin);
        (await admin.PutAsJsonAsync($"/admin/api/keys/{keyId}/model-grants", new { patterns = new[] { "openrouter:*" } }))
            .EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/admin/api/providers/openrouter/access/{tenantId}", new { open = true }))
            .EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/admin/api/keys/{keyId}/provider-access/openrouter", new { open = true }))
            .EnsureSuccessStatusCode();

        (await ChatAsync(caller)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await WaitForSpendAsync(admin)).Should().Be(18m);

        var repriced = await admin.PatchAsJsonAsync($"/admin/api/models/{Uri.EscapeDataString(PaidModel)}", new
        {
            model = new { id = PaidModel, url = "http://127.0.0.1:18080", maxContextLength = 8192, provider = "openrouter" },
            pricing = new { inputPricePerMillionTokens = 500_000m, outputPricePerMillionTokens = 500_000m },
        });
        repriced.StatusCode.Should().Be(HttpStatusCode.OK, await repriced.Content.ReadAsStringAsync());

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var dryRun = await admin.PostAsJsonAsync("/admin/api/usage/reprice", new { modelId = PaidModel, from = today, to = today });
        dryRun.StatusCode.Should().Be(HttpStatusCode.OK, await dryRun.Content.ReadAsStringAsync());
        using (var doc = JsonDocument.Parse(await dryRun.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("dryRun").GetBoolean().Should().BeTrue("a reprice that does not say otherwise writes nothing");
            doc.RootElement.GetProperty("applied").GetBoolean().Should().BeFalse();
            doc.RootElement.GetProperty("events").GetInt32().Should().Be(1);
            doc.RootElement.GetProperty("oldTotalUsd").GetDecimal().Should().Be(18m);
            doc.RootElement.GetProperty("newTotalUsd").GetDecimal().Should().Be(9m);
        }

        (await WaitForSpendAsync(admin)).Should().Be(18m, "the dry run changed nothing");

        var applied = await admin.PostAsJsonAsync(
            "/admin/api/usage/reprice",
            new { modelId = PaidModel, from = today, to = today, dryRun = false });
        applied.StatusCode.Should().Be(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        using (var doc = JsonDocument.Parse(await applied.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("applied").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("repriced").GetInt32().Should().Be(1);
        }

        (await WaitForSpendAsync(admin)).Should().Be(9m);
        using (var access = JsonDocument.Parse(await admin.GetStringAsync($"/admin/api/keys/{keyId}/provider-access")))
        {
            access.RootElement.GetProperty("providers").EnumerateArray()
                .Single(p => p.GetProperty("provider").GetString() == "openrouter")
                .GetProperty("spentThisMonthUsd").GetDecimal().Should().Be(9m, "the caps read the rebuilt provider spend");
        }

        using (var usage = JsonDocument.Parse(await admin.GetStringAsync($"/admin/api/usage/events?from={today}&to={today}")))
        {
            usage.RootElement.GetProperty("events")[0].GetProperty("totalCost").GetDecimal().Should().Be(9m);
        }

        (await admin.PostAsJsonAsync("/admin/api/usage/reprice", new { modelId = "no-such-model", from = today, to = today }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>With no company key nothing is read, so there is nothing to compare, and the report says so.</summary>
    [Fact]
    public async Task Reconciliation_WithoutReadings_SaysWhyThereIsNoComparison()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);

        using var doc = JsonDocument.Parse(await admin.GetStringAsync("/admin/api/providers/deepseek/reconciliation"));

        doc.RootElement.GetProperty("readings").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("drifting").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("unavailable").GetString().Should().Contain("two balance readings");
        doc.RootElement.GetProperty("threshold").GetProperty("percent").GetDecimal().Should().Be(10m);
    }

    private static async Task<decimal> WaitForSpendAsync(HttpClient admin)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var doc = JsonDocument.Parse(await admin.GetStringAsync("/admin/api/providers/openrouter/spend?days=7"));
            if (doc.RootElement.GetProperty("requests").GetInt32() > 0)
            {
                return doc.RootElement.GetProperty("totalUsd").GetDecimal();
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The request's cost never reached the provider spend.");
    }

    [Fact]
    public async Task ABackend_KeepsItsPriorityAndUpstreamName()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var admin = CreateClient(factory, AdminKey);

        var created = await admin.PostAsJsonAsync("/admin/api/models", new
        {
            model = new
            {
                id = "Qwen3-27B",
                backends = new object[]
                {
                    new { name = "gpu", url = "http://127.0.0.1:18080", maxContextLength = 8192 },
                    new
                    {
                        name = "standby",
                        url = "http://127.0.0.1:18081",
                        maxContextLength = 8192,
                        priority = "standby",
                        upstreamModel = "qwen/qwen3-27b",
                    },
                },
            },
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

        using var doc = JsonDocument.Parse(await admin.GetStringAsync("/admin/api/backends"));
        var rows = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("items");
        var standby = rows.EnumerateArray().Single(b =>
            b.GetProperty("modelId").GetString() == "Qwen3-27B" && b.GetProperty("backend").GetString() == "standby");
        standby.GetProperty("priority").GetString().Should().Be("standby");
        standby.GetProperty("upstreamModel").GetString().Should().Be("qwen/qwen3-27b");
        standby.GetProperty("provider").GetString().Should().Be("self-hosted");

        var bad = await admin.PostAsJsonAsync("/admin/api/models/Qwen3-27B/backends", new
        {
            backend = new { name = "third", url = "http://127.0.0.1:18082", priority = "backup" },
        });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

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
            pricing = new { inputPricePerMillionTokens = 1_000_000m, outputPricePerMillionTokens = 1_000_000m },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> ChatAsync(HttpClient caller)
    {
        using var body = new StringContent(
            "{\"model\":\"" + PaidModel + "\",\"stream\":false,\"max_tokens\":10}",
            System.Text.Encoding.UTF8,
            "application/json");
        return await caller.PostAsync("/v1/chat/completions", body);
    }

    private static async Task<decimal?> KeyCapAsync(HttpClient admin, Guid keyId)
    {
        using var doc = JsonDocument.Parse(await admin.GetStringAsync($"/admin/api/keys/{keyId}/provider-access"));
        var cap = doc.RootElement.GetProperty("providers").EnumerateArray()
            .Single(p => p.GetProperty("provider").GetString() == "openrouter")
            .GetProperty("monthlyCapUsd");
        return cap.ValueKind == JsonValueKind.Null ? null : cap.GetDecimal();
    }

    /// <summary>The admin key's own tenant: the only one there is, read from the provider's access rows.</summary>
    private static async Task<Guid> OwnTenantIdAsync(HttpClient admin)
    {
        using var doc = JsonDocument.Parse(await admin.GetStringAsync("/admin/api/providers/openrouter/access"));
        return doc.RootElement.GetProperty("tenants").EnumerateArray().First().GetProperty("tenantId").GetGuid();
    }

    private static async Task<(Guid KeyId, string Secret)> CreateInferenceKeyAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/admin/api/keys", new { role = "Inference" });
        response.EnsureSuccessStatusCode();
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (created.RootElement.GetProperty("id").GetGuid(), created.RootElement.GetProperty("secret").GetString()!);
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
