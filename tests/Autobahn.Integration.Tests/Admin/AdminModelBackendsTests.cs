using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Autobahn.Integration.Tests.Support;

namespace Autobahn.Integration.Tests.Admin;

/// <summary>
/// The backend calls of the admin API, end to end against a real SQLite database with the
/// migrations applied: a model registered with one url gains a second backend, loses both, and is
/// still there.
/// </summary>
public sealed class AdminModelBackendsTests
{
    [Fact]
    public async Task BackendCalls_AddStopKeepAndRemove_WithoutDisturbingTheModel()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithSqliteDatabase();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        var client = factory.CreateAdminClient();

        var modelId = "multi-" + Guid.NewGuid().ToString("N")[..8];
        var path = "/admin/api/models/" + Uri.EscapeDataString(modelId);

        // Registered the way every existing model was: one url.
        (await client.PostAsJsonAsync("/admin/api/models", new
        {
            model = new { id = modelId, url = "http://ordinary.invalid:8000", aliases = Array.Empty<string>(), maxContextLength = 262144 },
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var added = await client.PostAsJsonAsync(path + "/backends", new
        {
            backend = new
            {
                name = "long",
                url = "http://long.invalid:8000",
                maxContextLength = 1000000,
                labels = new Dictionary<string, string> { ["managed-by"] = "fleet" },
            },
            apiKey = "sk-upstream-0123456789abcdef",
        });
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());

        (await client.PostAsJsonAsync(path + "/backends", new { backend = new { name = "long", url = "http://again.invalid" } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var model = await FindModelAsync(client, modelId);
        model.GetProperty("url").GetString().Should().Be("http://ordinary.invalid:8000");
        model.GetProperty("maxContextLength").GetInt32().Should().Be(1000000);
        var backends = model.GetProperty("backends").EnumerateArray().ToList();
        backends.Select(b => b.GetProperty("name").GetString()).Should().Equal("default", "long");
        backends[1].GetProperty("upstreamAuth").GetProperty("secretRef").GetString()
            .Should().Be($"file:model:{modelId}:backend:long");
        backends[1].GetProperty("labels").GetProperty("managed-by").GetString().Should().Be("fleet");

        (await client.PostAsync(path + "/backends/long/stop", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = (await client.GetFromJsonAsync<JsonElement>("/admin/api/backends"))
            .EnumerateArray()
            .Where(r => r.GetProperty("modelId").GetString() == modelId)
            .ToDictionary(r => r.GetProperty("backend").GetString()!);
        rows.Keys.Should().BeEquivalentTo("default", "long");
        rows["long"].GetProperty("state").GetString().Should().Be("stopped");
        rows["long"].GetProperty("inFlight").GetInt32().Should().Be(0);
        rows["long"].GetProperty("maxContextLength").GetInt32().Should().Be(1000000);
        rows["default"].GetProperty("state").GetString().Should().Be("serving");

        // The console's edit: the model in the one-url shape. The list must survive it, and so
        // must the stop.
        var edited = await client.PatchAsJsonAsync(path, new
        {
            model = new { id = modelId, url = "http://ordinary.invalid:8000", aliases = new[] { modelId + "-alias" }, maxContextLength = 8192 },
        });
        edited.StatusCode.Should().Be(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        model = await FindModelAsync(client, modelId);
        model.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()).Should().Equal(modelId + "-alias");
        backends = model.GetProperty("backends").EnumerateArray().ToList();
        backends.Should().HaveCount(2);
        backends[0].GetProperty("maxContextLength").GetInt32().Should().Be(262144);
        backends[1].GetProperty("state").GetString().Should().Be("stopped");

        (await client.PatchAsJsonAsync(path, new
        {
            model = new { id = modelId, url = "http://elsewhere.invalid", aliases = Array.Empty<string>() },
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.PatchAsJsonAsync(path + "/backends/long", new { backend = new { maxContextLength = 500000 } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        model = await FindModelAsync(client, modelId);
        var patched = model.GetProperty("backends").EnumerateArray().Single(b => b.GetProperty("name").GetString() == "long");
        patched.GetProperty("url").GetString().Should().Be("http://long.invalid:8000");
        patched.GetProperty("maxContextLength").GetInt32().Should().Be(500000);
        patched.GetProperty("upstreamAuth").GetProperty("secretRef").GetString().Should().NotBeNullOrEmpty();

        // Both backends go; the model stays registered with nothing to forward to.
        (await client.DeleteAsync(path + "/backends/long")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync(path + "/backends/default")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync(path + "/backends/default")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        model = await FindModelAsync(client, modelId);
        model.GetProperty("backends").GetArrayLength().Should().Be(0);
        model.GetProperty("state").GetString().Should().Be("serving");

        var listed = await client.GetFromJsonAsync<JsonElement>("/v1/models");
        listed.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString())
            .Should().NotContain(modelId, "a model with no backend in service is not offered to callers");

        (await client.DeleteAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<JsonElement> FindModelAsync(HttpClient client, string modelId)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/admin/api/models");
        return list.EnumerateArray()
            .Select(entry => entry.GetProperty("model"))
            .Single(m => m.GetProperty("id").GetString() == modelId);
    }
}
