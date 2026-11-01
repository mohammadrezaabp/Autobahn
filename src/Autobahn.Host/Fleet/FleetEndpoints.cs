using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Autobahn.Application.Abstractions;
using Autobahn.Application.Fleet;
using Autobahn.Domain.Identity;
using Autobahn.Infrastructure.Fleet;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;

namespace Autobahn.Host.Fleet;

public static class FleetEndpoints
{
    public static IServiceCollection AddFleetEndpoints(this IServiceCollection services, IConfiguration configuration)
    {
        var keyDirectory = Path.Combine(configuration["Fleet:DataDirectory"] ?? "data/fleet", "keys");
        Directory.CreateDirectory(keyDirectory);
        services.AddDataProtection().SetApplicationName("Autobahn").PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        services.AddSingleton<FleetSessionService>();
        services.AddSingleton<IManagedRouteSynchronizer, ManagedRouteSynchronizer>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("fleet-sign-in", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }

    public static WebApplication MapFleetEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/fleet", () => Results.Redirect("/admin/fleet/index.html"));
        app.MapGet("/admin/fleet/healthz", () => Results.Ok(new { status = "healthy", name = "Autobahn Fleet" }));
        var group = app.MapGroup("/admin/fleet/api").AddEndpointFilter<FleetEndpointFilter>();
        group.MapPost("/session", async (JsonNode body, HttpContext context, IApiKeyValidator validator, FleetSessionService sessions, CancellationToken ct) =>
        {
            var key = body["key"]?.ToString() ?? "";
            var result = await validator.ValidateAsync(key, ct);
            if (!result.IsSuccess || result.Role is not (ApiKeyRole.Admin or ApiKeyRole.Both)) throw new FleetException(401, "invalid_key", "Use a valid Autobahn admin key.");
            sessions.SignIn(context, key);
            return Results.Ok(new { actor = result.TenantSlug, role = "admin" });
        }).AllowAnonymous().RequireRateLimiting("fleet-sign-in");
        group.MapDelete("/session", (HttpContext context) => { context.Response.Cookies.Delete(FleetSessionService.CookieName, new CookieOptions { Path = "/admin/fleet" }); return Results.NoContent(); }).WithName("FleetSessionSignOut");
        group.MapGet("/me", (HttpContext context) => Results.Ok(new { actor = context.Items["FleetActor"], role = (bool)context.Items["FleetCanWrite"]! ? "operator" : "viewer", canWrite = context.Items["FleetCanWrite"], expiresAt = ((DateTimeOffset)context.Items["FleetExpires"]!).ToUnixTimeSeconds(), version = "1.0.0" }));
        group.MapGet("/nodes", (FleetPoller poller) => Results.Json(poller.Snapshot()));
        group.MapGet("/nodes/{id}/history", (string id, FleetPoller poller) => Results.Json(poller.History(id)));
        group.MapGet("/nodes/{id}/import", async (string id, IFleetAgentClient agents, CancellationToken ct) => Results.Json(await agents.CallAsync(id, HttpMethod.Get, "/v1/import", cancellationToken: ct)));
        group.MapGet("/store", async (FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.FromEveryHostAsync("/v1/models", "store", ct)));
        group.MapGet("/downloads", async (FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.FromEveryHostAsync("/v1/downloads", "downloads", ct)));
        group.MapGet("/serving", async (string? name, FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.FromEveryHostAsync("/v1/serving" + (name is null ? "" : "?name=" + Uri.EscapeDataString(name)), "serving", ct)));
        group.MapPost("/nodes/{id}/store/rescan", async (string id, HttpContext context, FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.ChangeAsync(id, "/v1/models/rescan", new JsonObject(), Actor(context), ct)));
        group.MapPost("/nodes/{id}/store/delete", async (string id, JsonNode body, HttpContext context, FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.ChangeAsync(id, "/v1/models/delete", body, Actor(context), ct)));
        group.MapPost("/nodes/{id}/downloads", async (string id, JsonNode body, HttpContext context, FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.ChangeAsync(id, "/v1/downloads", body, Actor(context), ct)));
        group.MapPost("/nodes/{id}/downloads/{job}/cancel", async (string id, string job, HttpContext context, FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.ChangeAsync(id, $"/v1/downloads/{Uri.EscapeDataString(job)}/cancel", new JsonObject(), Actor(context), ct)));
        group.MapGet("/nodes/{id}/servers", async (string id, IFleetAgentClient agents, CancellationToken ct) => { var answer = await agents.CallAsync(id, HttpMethod.Get, "/v1/servers", cancellationToken: ct); answer["canPropose"] = true; return Results.Json(answer); });
        group.MapGet("/inventory", async (FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.InventoryAsync(ct)));
        group.MapPost("/inventory/plan", async (HttpContext context, FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.ReconcileAsync(null, true, Actor(context), ct)));
        group.MapPost("/inventory/apply", async (JsonNode body, HttpContext context, FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.ReconcileAsync(body["commit"]?.ToString(), false, Actor(context), ct)));
        group.MapPost("/nodes/{id}/changes/plan", async (string id, JsonNode body, FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.PlanAsync(id, body, ct)));
        group.MapPost("/nodes/{id}/changes/propose", async (string id, JsonNode body, HttpContext context, FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.ProposeAsync(id, body, Actor(context), ct)));
        group.MapPost("/nodes/{id}/servers/plan", async (string id, JsonNode body, FleetControlService fleet, CancellationToken ct) =>
        {
            var plan = await fleet.PlanAsync(id, Single(body), ct); var add = plan["adds"]!.AsArray()[0]!;
            return Results.Json(new { plan = add["plan"], path = add["path"], content = add["content"] });
        });
        group.MapPost("/nodes/{id}/servers/propose", async (string id, JsonNode body, HttpContext context, FleetControlService fleet, CancellationToken ct) =>
        { var result = await fleet.ProposeAsync(id, Single(body), Actor(context), ct); result["path"] = $"inventory/servers/{body["name"]}.json"; return Results.Json(result); });
        group.MapGet("/changes", async (FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.ChangesAsync(ct)));
        group.MapGet("/changes/{id}", async (string id, IFleetStateStore state, CancellationToken ct) => await state.ReadAsync("changes", id, ct) is { } entry ? Results.Json(entry) : Results.NotFound());
        group.MapPost("/changes/{id}/apply", async (string id, HttpContext context, FleetControlService fleet, CancellationToken ct) => Results.Json(await fleet.ApplyAsync(id, Actor(context), ct)));
        group.MapGet("/audit", async (IFleetStateStore state, CancellationToken ct) => Results.Ok(new { time = FleetPoller.Now, entries = await state.ListAsync("audit", ct) }));
        group.MapGet("/hf/models", async (string? search, IHttpClientFactory clients, CancellationToken ct) =>
        {
            var entries = await clients.CreateClient("fleet-catalog").GetFromJsonAsync<JsonArray>("https://huggingface.co/api/models?limit=30&full=true&search=" + Uri.EscapeDataString(search ?? ""), ct) ?? [];
            return Results.Ok(new { models = entries.Select(x => new { id = x?["id"]?.ToString(), downloads = x?["downloads"]?.GetValue<long?>(), likes = x?["likes"]?.GetValue<long?>(), gated = x?["gated"]?.ToString() is not (null or "false"), pipelineTag = x?["pipeline_tag"]?.ToString() ?? "", lastModified = x?["lastModified"]?.ToString() ?? "" }) });
        });
        group.MapGet("/hf/models/{owner}/{name}", async (string owner, string name, string? revision, IHttpClientFactory clients, CancellationToken ct) =>
        {
            var repo = owner + "/" + name; Autobahn.Domain.Fleet.RepositoryId.Validate(repo);
            var info = await clients.CreateClient("fleet-catalog").GetFromJsonAsync<JsonNode>("https://huggingface.co/api/models/" + repo + "/revision/" + Uri.EscapeDataString(string.IsNullOrEmpty(revision) ? "main" : revision) + "?blobs=true", ct) ?? new JsonObject();
            var files = info["siblings"]?.AsArray() ?? [];
            return Results.Ok(new { id = repo, sha = info["sha"]?.ToString(), gated = info["gated"]?.ToString() is not (null or "false"), files = files.Count, totalBytes = files.All(x => x?["size"] is not null) ? files.Sum(x => x!["size"]!.GetValue<long>()) : (long?)null });
        });
        return app;
    }

    private static string Actor(HttpContext context) => (string)context.Items["FleetActor"]!;
    private static JsonNode Single(JsonNode spec) => new JsonObject { ["add"] = new JsonArray(spec.DeepClone()), ["remove"] = new JsonArray() };
}
