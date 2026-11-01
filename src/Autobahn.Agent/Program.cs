using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autobahn.Application.Fleet;
using Autobahn.Domain.Common;
using Autobahn.Domain.Fleet;
using Autobahn.Infrastructure.Agent;
using Autobahn.Infrastructure.Fleet;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrEmpty(builder.Configuration["Urls"])) builder.WebHost.UseUrls("http://127.0.0.1:9455");
builder.WebHost.ConfigureKestrel(x => { x.AddServerHeader = false; x.Limits.MaxRequestBodySize = 65536; });
builder.Services.AddOptions<AgentOptions>().Bind(builder.Configuration.GetSection("Agent"))
    .Validate(x => x.Token.Length >= 32 && x.MinFreeBytes >= 0, "Configure an agent token of at least 32 characters and a nonnegative disk reserve.").ValidateOnStart();
var agentDirectory = Path.GetFullPath(builder.Configuration["Agent:DataDirectory"] ?? "data/agent");
Directory.CreateDirectory(agentDirectory);
builder.Services.AddDataProtection().SetApplicationName("Autobahn.Agent").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(agentDirectory, "keys")));
builder.Services.AddSingleton<IFleetStateStore>(new SqliteFleetStateStore(agentDirectory));
builder.Services.AddSingleton<IHostProcessRunner, ProcessRunner>(); builder.Services.AddSingleton<AgentGpuReader>();
builder.Services.AddSingleton<AgentContainerInspector>();
builder.Services.AddSingleton<AgentMutationGate>();
builder.Services.AddSingleton<AgentModelStore>(); builder.Services.AddSingleton<AgentServerManager>();
builder.Services.AddSingleton<AgentDownloadManager>(); builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentDownloadManager>());
builder.Services.AddHttpClient("agent-downloads", x => x.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddHttpClient("agent-probes", x => x.Timeout = TimeSpan.FromSeconds(5)).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    try
    {
        if (context.Request.Path != "/healthz")
        {
            var token = context.Request.Headers.Authorization.ToString();
            var expected = "Bearer " + context.RequestServices.GetRequiredService<IOptions<AgentOptions>>().Value.Token;
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(token)), SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { code = "unauthorized", message = "A valid node token is required." }); return; }
        }
        await next(context);
    }
    catch (DomainException ex) { context.Response.StatusCode = ex.Code == "not_found" ? 404 : 409; await context.Response.WriteAsJsonAsync(new { code = ex.Code, message = ex.Message }); }
    catch (FleetException ex) { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(new { code = ex.Code, message = ex.Message }); }
    catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
    { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { code = "invalid_request", message = "The request is invalid." }); }
});
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", name = "Autobahn Agent" }));
app.MapGet("/v1/status", async (AgentGpuReader gpu, AgentModelStore models, AgentServerManager servers, IFleetStateStore state, IOptions<AgentOptions> config, CancellationToken ct) =>
{
    var gpuReport = await gpu.ReadAsync(ct);
    JsonArray running;
    try
    {
        var listed = await servers.ListAsync(ct);
        AgentRuntimeDiscovery.Attach(gpuReport, await servers.ContainersAsync(ct), await state.ListAsync("servers", ct));
        running = new JsonArray(listed["servers"]!.AsArray().Select(s => (JsonNode?)new JsonObject { ["pid"] = 0, ["model"] = s!["model"]!.DeepClone(), ["servedName"] = s["servedName"]!.DeepClone(),
            ["port"] = s["port"]!.DeepClone(), ["hostPorts"] = new JsonArray(s["port"]!.DeepClone()), ["containerName"] = "autobahn-" + s["name"] }).ToArray());
    }
    catch (FleetException) { running = []; }
    return Results.Json(new { name = config.Value.Name, hostname = Environment.MachineName, agentVersion = "1.0.0", time = FleetPoller.Now, disk = models.Disk(), minFreeBytes = config.Value.MinFreeBytes, gpu = gpuReport, servers = running });
});
app.MapGet("/v1/models", async (AgentModelStore models, CancellationToken ct) => Results.Json(await models.ScanAsync(ct)));
app.MapPost("/v1/models/rescan", async (AgentModelStore models, CancellationToken ct) => { await models.ScanAsync(ct); return Results.Ok(new { scanning = false }); });
app.MapPost("/v1/models/delete", async (JsonNode body, AgentModelStore models, CancellationToken ct) => Results.Json(await models.DeleteAsync(body["path"]?.ToString() ?? "", body["confirm"]?.ToString() ?? "", ct)));
app.MapGet("/v1/downloads", async (IFleetStateStore store, CancellationToken ct) => Results.Json(new { jobs = await store.ListAsync("downloads", ct) }));
app.MapPost("/v1/downloads", async (JsonNode body, AgentDownloadManager downloads, CancellationToken ct) => Results.Json(await downloads.EnqueueAsync(body["repo"]?.ToString() ?? "", body["revision"]?.ToString() is { Length: > 0 } revision ? revision : "main", body["from"]?.ToString() ?? "", body["requestedBy"]?.ToString() ?? "", ct)));
app.MapPost("/v1/downloads/{id}/cancel", async (string id, AgentDownloadManager downloads, CancellationToken ct) => Results.Json(await downloads.CancelAsync(id, ct)));
app.MapGet("/v1/servers", async (AgentServerManager servers, CancellationToken ct) => Results.Json(await servers.ListAsync(ct)));
app.MapGet("/v1/servers/{name}", async (string name, IFleetStateStore state, CancellationToken ct) => await state.ReadAsync("servers", name, ct) is { } spec ? Results.Json(spec) : Results.NotFound());
app.MapGet("/v1/servers/{name}/key", async (string name, AgentServerManager servers, CancellationToken ct) => Results.Ok(new { key = await servers.KeyAsync(name, ct) }));
app.MapPost("/v1/servers", async (JsonNode body, AgentServerManager servers, CancellationToken ct) => Results.Json(await servers.ApplyAsync(FleetControlService.Spec(body["spec"] ?? body), body["dryRun"]?.GetValue<bool>() == true, body["leaving"]?.AsArray().Select(x => x!.ToString()).ToArray() ?? [], ct)));
app.MapPost("/v1/servers/{name}/stop", async (string name, AgentServerManager servers, CancellationToken ct) => Results.Json(await servers.StopAsync(name, false, ct)));
app.MapPost("/v1/servers/{name}/remove", async (string name, AgentServerManager servers, CancellationToken ct) => Results.Json(await servers.StopAsync(name, true, ct)));
app.MapGet("/v1/serving", async (string? name, AgentServerManager servers, CancellationToken ct) => Results.Json(await servers.ServingAsync(name, ct)));
app.MapGet("/v1/import", async (AgentContainerInspector containers, CancellationToken ct) => Results.Json(await containers.ImportAsync(ct)));
app.MapGet("/v1/files/manifest", async (string repo, string revision, AgentModelStore models, CancellationToken ct) =>
{
    RepositoryId.Validate(repo); var directory = await models.ResolveRepositoryAsync(repo, revision, ct);
    return Results.Json(new { sha = Path.GetFileName(directory), files = ModelPaths.Files(directory).Where(x => x.Name != ".autobahn-model.json").Select(f => new { path = Path.GetRelativePath(directory, f.FullName).Replace('\\', '/'), size = f.Length }) });
});
app.MapGet("/v1/files/content", async (string repo, string revision, string file, AgentModelStore models, CancellationToken ct) =>
{
    RepositoryId.Validate(repo); var directory = await models.ResolveRepositoryAsync(repo, revision, ct);
    if (file == ".autobahn-model.json") return Results.NotFound();
    var path = ModelPaths.Resolve(directory, file, true);
    return Results.File(path, "application/octet-stream", enableRangeProcessing: true);
});
app.Run();
