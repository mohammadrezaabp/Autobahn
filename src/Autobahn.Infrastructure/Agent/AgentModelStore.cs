using System.Text.Json.Nodes;
using Autobahn.Application.Fleet;
using Microsoft.Extensions.Options;

namespace Autobahn.Infrastructure.Agent;

public sealed class AgentModelStore(IOptions<AgentOptions> options, IFleetStateStore state, AgentContainerInspector containers, AgentMutationGate mutations)
{
    public string Root { get; } = Path.GetFullPath(options.Value.ModelDirectory);

    public JsonNode Disk()
    {
        Directory.CreateDirectory(Root);
        var drive = new DriveInfo(Path.GetPathRoot(Root)!);
        return new JsonObject { ["path"] = Root, ["totalBytes"] = drive.TotalSize, ["usedBytes"] = drive.TotalSize - drive.AvailableFreeSpace, ["freeBytes"] = drive.AvailableFreeSpace };
    }

    public async Task<JsonNode> ScanAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);
        var running = await state.ListAsync("servers", cancellationToken);
        var jobs = await state.ListAsync("downloads", cancellationToken);
        var models = new JsonArray();
        foreach (var marker in ModelPaths.Files(Root).Where(f => f.Name == ".autobahn-model.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(Root, marker.DirectoryName!).Replace('\\', '/');
            var folder = ModelPaths.Resolve(Root, relative, true);
            var metadata = JsonNode.Parse(await File.ReadAllTextAsync(marker.FullName, cancellationToken))!;
            var repo = metadata["repo"]!.ToString();
            var files = ModelPaths.Files(folder).Where(f => f.Name != ".autobahn-model.json").ToArray();
            models.Add(new JsonObject { ["path"] = relative, ["name"] = repo, ["kind"] = "hub", ["repoId"] = repo, ["repoType"] = "model", ["sizeBytes"] = files.Sum(f => f.Length), ["fileCount"] = files.Length,
                ["modifiedAt"] = new DateTimeOffset(marker.LastWriteTimeUtc).ToUnixTimeSeconds(), ["incomplete"] = metadata["complete"]?.GetValue<bool>() != true,
                ["revisions"] = new JsonArray(metadata["sha"]?.DeepClone()), ["inUseBy"] = new JsonArray(running.Where(x => x["model"]?.ToString() == repo).Select(x => x["name"]!.DeepClone()).ToArray()),
                ["downloading"] = jobs.Any(x => x["repo"]?.ToString() == repo && x["state"]?.ToString() is "running" or "queued") });
        }
        // Plain weight directories are useful when a model was placed on the host manually.
        foreach (var folder in Directory.EnumerateDirectories(Root))
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
            var files = ModelPaths.Files(folder).ToArray();
            if (files.Any(f => f.Name == ".autobahn-model.json") || files.Length == 0) continue;
            var name = Path.GetFileName(folder);
            models.Add(new JsonObject { ["path"] = name, ["name"] = name, ["kind"] = "flat", ["repoId"] = "", ["repoType"] = "model", ["sizeBytes"] = files.Sum(f => f.Length), ["fileCount"] = files.Length,
                ["modifiedAt"] = new DateTimeOffset(Directory.GetLastWriteTimeUtc(folder)).ToUnixTimeSeconds(), ["incomplete"] = false, ["revisions"] = new JsonArray(),
                ["inUseBy"] = new JsonArray(running.Where(x => x["model"]?.ToString() == name).Select(x => x["name"]!.DeepClone()).ToArray()), ["downloading"] = false });
        }
        return new JsonObject { ["name"] = options.Value.Name, ["disk"] = Disk(), ["minFreeBytes"] = options.Value.MinFreeBytes, ["scannedAt"] = Fleet.FleetPoller.Now,
            ["scanning"] = false, ["allowDelete"] = options.Value.AllowDelete, ["copyFrom"] = new JsonArray(options.Value.Peers.Select(x => (JsonNode?)JsonValue.Create(x.Id)).ToArray()),
            ["models"] = models, ["roots"] = new JsonObject { ["hub"] = new JsonArray(Root), ["flat"] = new JsonArray(), ["stray"] = new JsonArray() } };
    }

    public async Task<JsonNode> DeleteAsync(string relative, string confirmation, CancellationToken cancellationToken)
    {
        await mutations.Semaphore.WaitAsync(cancellationToken);
        try { return await DeleteCoreAsync(relative, confirmation, cancellationToken); }
        finally { mutations.Semaphore.Release(); }
    }

    private async Task<JsonNode> DeleteCoreAsync(string relative, string confirmation, CancellationToken cancellationToken)
    {
        if (!options.Value.AllowDelete) throw new FleetException(403, "delete_disabled", "Model deletion is disabled on this host.");
        var snapshot = await ScanAsync(cancellationToken);
        var model = snapshot["models"]!.AsArray().SingleOrDefault(x => x?["path"]?.ToString() == relative) ?? throw new FleetException(404, "not_found", "The model is not in the store.");
        if (confirmation != model["name"]!.ToString()) throw new FleetException(400, "confirmation_required", "Type the model's name to delete it.");
        if (model["inUseBy"]!.AsArray().Count > 0 || model["downloading"]!.GetValue<bool>()) throw new FleetException(409, "model_in_use", "Stop its model servers and downloads before deleting it.");
        var full = ModelPaths.Resolve(Root, relative, true);
        await containers.EnsureNotMountedAsync(full, cancellationToken);
        // Resolve every descendant before removing the directory; never traverse a junction.
        foreach (var item in Directory.EnumerateFileSystemEntries(full, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            ModelPaths.Resolve(Root, Path.GetRelativePath(Root, item).Replace('\\', '/'), true);
        Directory.Delete(full, true);
        return new JsonObject { ["deleted"] = relative, ["name"] = model["name"]!.DeepClone(), ["freedBytes"] = model["sizeBytes"]!.DeepClone() };
    }

    public async Task<string> ResolveRepositoryAsync(string repo, string revision, CancellationToken cancellationToken)
    {
        var scan = await ScanAsync(cancellationToken);
        var model = scan["models"]!.AsArray().FirstOrDefault(x => (x?["repoId"]?.ToString() == repo || (x?["kind"]?.ToString() == "flat" && x["path"]?.ToString() == repo)) && x!["incomplete"]?.GetValue<bool>() == false && x["downloading"]?.GetValue<bool>() == false &&
            (revision == "main" || x["revisions"]?.AsArray().Any(r => r?.ToString() == revision) == true));
        return model is null ? throw new FleetException(409, "model_missing", "Download the complete model revision before starting a server.") : ModelPaths.Resolve(Root, model["path"]!.ToString(), true);
    }
}
