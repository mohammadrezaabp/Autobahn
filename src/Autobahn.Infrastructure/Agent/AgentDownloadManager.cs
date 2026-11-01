using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Autobahn.Application.Fleet;
using Autobahn.Domain.Fleet;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autobahn.Infrastructure.Agent;

public sealed class AgentDownloadManager(IHttpClientFactory clients, IOptions<AgentOptions> options, IFleetStateStore state, AgentModelStore models, ILogger<AgentDownloadManager> logger, AgentMutationGate mutations) : BackgroundService
{
    private readonly Channel<DownloadJob> _queue = Channel.CreateBounded<DownloadJob>(128);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();
    private readonly SemaphoreSlim _enqueue = mutations.Semaphore;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private Task PersistAsync(DownloadJob job, CancellationToken cancellationToken) => state.WriteAsync("downloads", job.Id, JsonSerializer.SerializeToNode(job, Json)!, cancellationToken);

    public async Task<JsonNode> EnqueueAsync(string repo, string revision, string source, string actor, CancellationToken cancellationToken)
    {
        RepositoryId.Validate(repo);
        if (string.IsNullOrWhiteSpace(revision) || revision.StartsWith('-') || revision.Length > 128 || revision.Contains('\0')) throw new FleetException(400, "invalid_revision", "Choose a valid revision.");
        if (source.Length > 0 && !options.Value.Peers.Any(x => x.Id == source)) throw new FleetException(400, "unknown_peer", "The source host is not a configured peer.");
        await _enqueue.WaitAsync(cancellationToken);
        try
        {
            if ((await state.ListAsync("downloads", cancellationToken)).Any(x => x["repo"]?.ToString() == repo && x["state"]?.ToString() is "queued" or "running"))
                throw new FleetException(409, "already_downloading", "This model already has an active download.");
            var job = new DownloadJob { Repo = repo, Revision = revision, Source = source, RequestedBy = actor };
            var cancel = new CancellationTokenSource(); _cancellations[job.Id] = cancel;
            await PersistAsync(job, cancellationToken);
            if (!_queue.Writer.TryWrite(job))
            {
                _cancellations.TryRemove(job.Id, out _); cancel.Dispose();
                job.Finish(DownloadState.Failed, "queue_full", "The download queue is full."); await PersistAsync(job, cancellationToken);
                throw new FleetException(429, "queue_full", "The download queue is full.");
            }
            return JsonSerializer.SerializeToNode(job, Json)!;
        }
        finally { _enqueue.Release(); }
    }

    public async Task<JsonNode> CancelAsync(string id, CancellationToken cancellationToken)
    {
        var job = await state.ReadAsync("downloads", id, cancellationToken) ?? throw new FleetException(404, "unknown_download", "The download does not exist.");
        if (_cancellations.TryGetValue(id, out var cancel)) cancel.Cancel();
        return job;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var stale in await state.ListAsync("downloads", stoppingToken))
        {
            if (stale["state"]?.ToString() is not ("queued" or "running")) continue;
            stale["state"] = "interrupted"; stale["finishedAt"] = Fleet.FleetPoller.Now; stale["message"] = "The agent restarted. Start a new download to resume partial files.";
            await state.WriteAsync("downloads", stale["id"]!.ToString(), stale, stoppingToken);
        }
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!_cancellations.TryGetValue(job.Id, out var cancel)) continue;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, cancel.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested(); job.Start(); await PersistAsync(job, stoppingToken);
                await DownloadAsync(job, linked.Token);
                job.Finish(DownloadState.Completed);
            }
            catch (OperationCanceledException) { job.Finish(stoppingToken.IsCancellationRequested ? DownloadState.Interrupted : DownloadState.Cancelled); }
            catch (Exception ex)
            {
                job.Finish(DownloadState.Failed, ex is FleetException failure ? failure.Code : "download_failed", ex is FleetException fleetFailure ? fleetFailure.Message : "The transfer failed. Check agent connectivity and free disk space.");
                logger.LogWarning("Download {Job} failed with {Code}", job.Id, job.ErrorCode);
            }
            finally { await PersistAsync(job, CancellationToken.None); _cancellations.TryRemove(job.Id, out _); cancel.Dispose(); }
        }
    }

    private async Task DownloadAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        var client = clients.CreateClient("agent-downloads");
        JsonNode manifest; string sha; JsonArray files;
        var peer = options.Value.Peers.SingleOrDefault(x => x.Id == job.Source);
        if (peer is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, peer.Address.TrimEnd('/') + "/v1/files/manifest?repo=" + Uri.EscapeDataString(job.Repo) + "&revision=" + Uri.EscapeDataString(job.Revision));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", peer.Token);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new FleetException(409, "peer_model_unavailable", "The peer does not have this complete model revision.");
            manifest = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
            sha = manifest["sha"]!.ToString(); files = manifest["files"]!.AsArray();
        }
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://huggingface.co/api/models/" + job.Repo + "/revision/" + Uri.EscapeDataString(job.Revision) + "?blobs=true");
            if (!string.IsNullOrWhiteSpace(options.Value.HuggingFaceToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.HuggingFaceToken);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new FleetException(502, "catalog_error", $"The model catalog returned {(int)response.StatusCode}.");
            manifest = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
            sha = manifest["sha"]!.ToString();
            files = new JsonArray(manifest["siblings"]!.AsArray().Select(x => (JsonNode?)new JsonObject
            { ["path"] = x!["rfilename"]!.DeepClone(), ["size"] = x["size"]?.DeepClone() ?? x["lfs"]?["size"]?.DeepClone(), ["sha256"] = x["lfs"]?["sha256"]?.DeepClone() }).ToArray());
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(sha, "^[a-fA-F0-9]{7,64}$")) throw new FleetException(502, "invalid_manifest", "The model manifest has an invalid revision.");
        if (files.Count == 0 || files.Count > 100000 || files.Any(x => x?["size"] is null)) throw new FleetException(409, "unknown_size", "The model manifest must provide file sizes.");
        var relative = job.Repo + "/" + sha;
        var directory = ModelPaths.Resolve(models.Root, relative); Directory.CreateDirectory(directory);
        var total = files.Sum(x => x!["size"]!.GetValue<long>());
        long required = 0;
        foreach (var entry in files)
        {
            var target = ModelPaths.Resolve(directory, entry!["path"]!.ToString());
            var size = entry["size"]!.GetValue<long>();
            if (size < 0) throw new FleetException(502, "invalid_manifest", "The model manifest has an invalid file size.");
            var existing = File.Exists(target) ? new FileInfo(target).Length : File.Exists(target + ".part") ? new FileInfo(target + ".part").Length : 0;
            required = checked(required + Math.Max(0, size - existing));
        }
        if (total < 0 || required > models.Disk()["freeBytes"]!.GetValue<long>() - options.Value.MinFreeBytes) throw new FleetException(409, "disk_full", "The download would consume the host's reserved free space.");
        job.Describe(total, files.Count, sha, relative);
        var marker = Path.Combine(directory, ".autobahn-model.json");
        await File.WriteAllTextAsync(marker, new JsonObject { ["repo"] = job.Repo, ["sha"] = sha, ["complete"] = false }.ToJsonString(), cancellationToken);
        long done = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = file!["path"]!.ToString();
            if (filePath == ".autobahn-model.json" || filePath.EndsWith(".part", StringComparison.Ordinal)) throw new FleetException(409, "reserved_file", "The manifest uses an agent-reserved file name.");
            var target = ModelPaths.Resolve(directory, filePath); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var size = file["size"]!.GetValue<long>(); if (size < 0) throw new FleetException(502, "invalid_manifest", "The model manifest has a negative file size.");
            var part = target + ".part"; ModelPaths.Resolve(directory, filePath + ".part");
            var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset > size) { File.Delete(part); offset = 0; }
            if (!(File.Exists(target) && new FileInfo(target).Length == size))
            {
                var url = peer is not null ? peer.Address.TrimEnd('/') + "/v1/files/content?repo=" + Uri.EscapeDataString(job.Repo) + "&revision=" + sha + "&file=" + Uri.EscapeDataString(filePath)
                    : "https://huggingface.co/" + job.Repo + "/resolve/" + sha + "/" + string.Join('/', filePath.Split('/').Select(Uri.EscapeDataString));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                var token = peer?.Token ?? options.Value.HuggingFaceToken;
                if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode) throw new FleetException(502, "transfer_error", $"The model file transfer returned {(int)response.StatusCode}.");
                if (offset > 0 && response.StatusCode != HttpStatusCode.PartialContent) offset = 0;
                await using (var output = new FileStream(part, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    var buffer = new byte[65536]; var received = offset; var checkpoint = DateTimeOffset.UtcNow;
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer, cancellationToken); if (read == 0) break;
                        received += read;
                        if (received > size) throw new FleetException(502, "size_mismatch", "A model file exceeds its declared size.");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        job.Progress(done + received);
                        if (DateTimeOffset.UtcNow - checkpoint > TimeSpan.FromSeconds(2))
                        {
                            if (models.Disk()["freeBytes"]!.GetValue<long>() < options.Value.MinFreeBytes) throw new FleetException(409, "disk_full", "The host's reserved free space was reached.");
                            await PersistAsync(job, cancellationToken); checkpoint = DateTimeOffset.UtcNow;
                        }
                    }
                    if (received != size) throw new FleetException(502, "size_mismatch", "A model file ended before its declared size.");
                }
                File.Move(part, target, true);
            }
            if (file["sha256"]?.ToString() is { Length: 64 } hash)
            {
                await using var stream = File.OpenRead(target);
                if (!Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                { File.Delete(target); throw new FleetException(502, "checksum_mismatch", "A downloaded model file failed its checksum."); }
            }
            done += size; job.Progress(done); await PersistAsync(job, cancellationToken);
        }
        await File.WriteAllTextAsync(marker, new JsonObject { ["repo"] = job.Repo, ["sha"] = sha, ["complete"] = true }.ToJsonString(), cancellationToken);
    }
}
