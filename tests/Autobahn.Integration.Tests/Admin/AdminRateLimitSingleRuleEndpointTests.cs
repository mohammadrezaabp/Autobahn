using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Autobahn.Integration.Tests.Support;

namespace Autobahn.Integration.Tests.Admin;

/// <summary>
/// Writing one rule. The whole-set PUT replaces every rule, so a caller that wants to add one has
/// to carry all the others back unharmed; these routes change the rule they name and nothing else.
/// </summary>
public sealed class AdminRateLimitSingleRuleEndpointTests
{
    private const string AdminKey = "sk-Autobahn-integration-admin-key";
    private const string Rules = "/admin/api/rate-limits/rules";

    [Fact]
    public async Task PuttingARule_AddsIt_AndLeavesEverythingElseAsStored()
    {
        await using var factory = CreateFactory();
        var admin = await ClientAsync(factory);
        var before = await ReadAsync(admin);

        var put = await admin.PutAsJsonAsync(
            Rules,
            new { scope = "tenant", target = "personal-keys", rpm = 0, burst = 0, maxConcurrentStreams = 0, tpm = 2_000_000 });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var answer = await put.Content.ReadFromJsonAsync<JsonElement>();
        var version = answer.GetProperty("version").GetInt64();
        version.Should().BeGreaterThan(before.GetProperty("version").GetInt64());
        put.Headers.ETag!.Tag.Should().Be($"\"{version}\"");
        answer.GetProperty("change").GetProperty("kind").GetString().Should().Be("added");
        answer.GetProperty("change").GetProperty("ruleId").GetString().Should().Be("tenant:personal-keys");

        var after = await ReadAsync(admin);
        after.GetProperty("enabled").GetBoolean().Should().Be(before.GetProperty("enabled").GetBoolean());
        after.GetProperty("default").GetRawText().Should().Be(before.GetProperty("default").GetRawText());
        Identities(after).Should().BeEquivalentTo(Identities(before).Append("tenant:personal-keys"));
        Rule(after, "tenant", "personal-keys").GetProperty("tpm").GetInt32().Should().Be(2_000_000);
    }

    [Fact]
    public async Task PuttingAnExistingRule_ReplacesItsNumbers_AndKeepsItsWindowsWhenNoneAreSent()
    {
        await using var factory = CreateFactory();
        var admin = await ClientAsync(factory);
        var soon = DateTimeOffset.UtcNow.AddDays(30);
        (await admin.PutAsJsonAsync(Rules, new
        {
            scope = "model",
            target = "local-mock",
            rpm = 600,
            burst = 60,
            maxConcurrentStreams = 0,
            schedule = new[]
            {
                new { name = "launch", kind = "once", rpm = 900, burst = 0, maxConcurrentStreams = 0, from = soon, until = soon.AddDays(1) },
            },
        })).EnsureSuccessStatusCode();

        var put = await admin.PutAsJsonAsync(
            Rules,
            new { scope = "model", target = "local-mock", rpm = 300, burst = 30, maxConcurrentStreams = 0 });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var change = (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("change");
        change.GetProperty("kind").GetString().Should().Be("changed");
        change.GetProperty("before").GetString().Should().StartWith("600rpm+60burst");
        change.GetProperty("after").GetString().Should().StartWith("300rpm+30burst");

        var rule = Rule(await ReadAsync(admin), "model", "local-mock");
        rule.GetProperty("rpm").GetInt32().Should().Be(300);
        rule.GetProperty("schedule").EnumerateArray().Single().GetProperty("name").GetString().Should().Be("launch");
    }

    [Fact]
    public async Task PuttingTheValuesARuleAlreadyHas_WritesNothing()
    {
        await using var factory = CreateFactory();
        var admin = await ClientAsync(factory);
        var rule = new { scope = "api_key", target = "6f1c0000-0000-0000-0000-000000000001", rpm = 30, burst = 0, maxConcurrentStreams = 2 };
        (await admin.PutAsJsonAsync(Rules, rule)).EnsureSuccessStatusCode();
        var version = (await ReadAsync(admin)).GetProperty("version").GetInt64();

        var again = await admin.PutAsJsonAsync(Rules, rule);

        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var answer = await again.Content.ReadFromJsonAsync<JsonElement>();
        answer.GetProperty("version").GetInt64().Should().Be(version);
        answer.GetProperty("change").ValueKind.Should().Be(JsonValueKind.Null);
        (await ReadAsync(admin)).GetProperty("version").GetInt64().Should().Be(version);
    }

    [Fact]
    public async Task DeletingARule_RemovesThatRuleOnly_AndAMissingOneIsNotFound()
    {
        await using var factory = CreateFactory();
        var admin = await ClientAsync(factory);
        (await admin.PutAsJsonAsync(Rules, new { scope = "model", target = "org/model-a", rpm = 100, burst = 0, maxConcurrentStreams = 0 })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync(Rules, new { scope = "tenant_model", target = "acme|org/model-a", rpm = 10, burst = 0, maxConcurrentStreams = 0 })).EnsureSuccessStatusCode();
        var before = Identities(await ReadAsync(admin));

        var delete = await admin.DeleteAsync(Rules + "?scope=tenant_model&target=" + Uri.EscapeDataString("acme|org/model-a"));

        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        (await delete.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("change").GetProperty("kind").GetString().Should().Be("removed");
        Identities(await ReadAsync(admin)).Should().BeEquivalentTo(before.Where(id => id != "tenant_model:acme|org/model-a"));

        (await admin.DeleteAsync(Rules + "?scope=tenant&target=nobody")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync(Rules + "?scope=tenant")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ARuleTheWholeSetWriteWouldRefuse_IsRefused_AndNothingIsStored()
    {
        await using var factory = CreateFactory();
        var admin = await ClientAsync(factory);
        var before = (await ReadAsync(admin)).GetRawText();

        // A rule that limits nothing.
        var put = await admin.PutAsJsonAsync(Rules, new { scope = "model", target = "local-mock", rpm = 0, burst = 0, maxConcurrentStreams = 0 });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(admin)).GetRawText().Should().Be(before);
    }

    [Fact]
    public async Task AWriteBasedOnAnOlderVersion_IsAConflict()
    {
        await using var factory = CreateFactory();
        var admin = await ClientAsync(factory);
        (await admin.PutAsJsonAsync(Rules, new { scope = "tenant", target = "acme", rpm = 100, burst = 0, maxConcurrentStreams = 0 })).EnsureSuccessStatusCode();

        var stale = new HttpRequestMessage(HttpMethod.Put, Rules)
        {
            Content = JsonContent.Create(new { scope = "tenant", target = "acme", rpm = 50, burst = 0, maxConcurrentStreams = 0 }),
        };
        stale.Headers.TryAddWithoutValidation("If-Match", "W/\"0\"");

        (await admin.SendAsync(stale)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        Rule(await ReadAsync(admin), "tenant", "acme").GetProperty("rpm").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task History_ShowsASingleRuleWriteAsOneChange_AndARefusalAsRefused()
    {
        await using var factory = CreateFactory();
        var admin = await ClientAsync(factory);
        (await admin.PutAsJsonAsync(Rules, new { scope = "tenant", target = "acme", rpm = 100, burst = 0, maxConcurrentStreams = 0 })).EnsureSuccessStatusCode();
        (await admin.DeleteAsync(Rules + "?scope=tenant&target=acme")).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync(Rules, new { scope = "model", target = "local-mock", rpm = 0, burst = 0, maxConcurrentStreams = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var entries = (await admin.GetFromJsonAsync<JsonElement>("/admin/api/rate-limits/history"))
            .GetProperty("entries").EnumerateArray().ToArray();

        entries.Should().HaveCount(3);
        entries[0].GetProperty("outcome").GetString().Should().Be("refused");
        entries[0].GetProperty("statusCode").GetInt32().Should().Be(400);
        entries[1].GetProperty("outcome").GetString().Should().Be("applied");
        entries[1].GetProperty("changes").EnumerateArray().Single().GetProperty("kind").GetString().Should().Be("removed");
        entries[2].GetProperty("changes").EnumerateArray().Single().GetProperty("ruleId").GetString().Should().Be("tenant:acme");
        entries[2].GetProperty("changes").EnumerateArray().Single().GetProperty("kind").GetString().Should().Be("added");
    }

    [Fact]
    public async Task TheRoutesNeedAnOperatorKey()
    {
        await using var factory = CreateFactory();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var anonymous = factory.CreateClient();

        (await anonymous.PutAsJsonAsync(Rules, new { scope = "tenant", target = "acme", rpm = 1, burst = 0, maxConcurrentStreams = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync(Rules + "?scope=tenant&target=acme")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient admin) =>
        await admin.GetFromJsonAsync<JsonElement>("/admin/api/rate-limits");

    private static string[] Identities(JsonElement config) =>
        [.. config.GetProperty("rules").EnumerateArray()
            .Select(r => r.GetProperty("scope").GetString() + ":" + r.GetProperty("target").GetString())];

    private static JsonElement Rule(JsonElement config, string scope, string target) =>
        config.GetProperty("rules").EnumerateArray().Single(r =>
            r.GetProperty("scope").GetString() == scope && r.GetProperty("target").GetString() == target);

    private static async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> factory)
    {
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var auditPath = Path.Combine(Path.GetTempPath(), "Autobahn-rl-one-rule-" + Guid.NewGuid().ToString("N"), "audit.jsonl");
        return GatewayWebApplicationFactory.CreateWithInMemoryDatabase(
            AdminKey,
            configureSettings: settings => settings["Gateway:Security:AuditLogPath"] = auditPath);
    }
}
