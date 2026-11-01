using System.Text.Json.Nodes;
using Autobahn.Application.Fleet;
using Autobahn.Infrastructure.Agent;
using Autobahn.Infrastructure.Fleet;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autobahn.Fleet.Tests;

public sealed class AgentDownloadTests
{
    [Fact]
    public async Task DownloadPinsTheRevisionVerifiesTheChecksumAndPublishesOnlyACompleteModel()
    {
        await ExerciseAsync("weights.bin", async (manager, state, models, root) =>
        {
            var queued = await manager.EnqueueAsync("example/chat", "main", "", "operator", default);
            var result = await FinishedAsync(state, queued["id"]!.ToString());
            result["state"]!.ToString().Should().Be("completed");
            result["doneBytes"]!.GetValue<long>().Should().Be(5);
            (await File.ReadAllBytesAsync(Path.Combine(root, "models", "example", "chat", DownloadHttpHandler.Revision, "weights.bin"))).Should().Equal(DownloadHttpHandler.Payload);
            var stored = (await models.ScanAsync(default))["models"]!.AsArray().Single()!;
            stored["incomplete"]!.GetValue<bool>().Should().BeFalse();
            stored["repoId"]!.ToString().Should().Be("example/chat");
        });
    }

    [Fact]
    public async Task AHostileManifestCannotWriteOutsideTheModelDirectory()
    {
        await ExerciseAsync("../../../../escape.bin", async (manager, state, _, root) =>
        {
            var queued = await manager.EnqueueAsync("example/chat", "main", "", "operator", default);
            (await FinishedAsync(state, queued["id"]!.ToString()))["state"]!.ToString().Should().Be("failed");
            File.Exists(Path.Combine(root, "escape.bin")).Should().BeFalse();
        });
    }

    private static async Task<JsonNode> FinishedAsync(IFleetStateStore state, string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var job = await state.ReadAsync("downloads", id, timeout.Token);
            if (job?["state"]?.ToString() is "completed" or "failed" or "cancelled") return job;
            await Task.Delay(25, timeout.Token);
        }
    }

    private static async Task ExerciseAsync(string filePath, Func<AgentDownloadManager, IFleetStateStore, AgentModelStore, string, Task> exercise)
    {
        var root = Path.Combine(Path.GetTempPath(), "autobahn-download-test-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new AgentOptions { ModelDirectory = Path.Combine(root, "models"), DataDirectory = Path.Combine(root, "state"), MinFreeBytes = 0 });
        var state = new SqliteFleetStateStore(options.Value.DataDirectory);
        var runner = Substitute.For<IHostProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>(), Arg.Any<int>()).Returns(Task.FromResult(""));
        using var mutations = new AgentMutationGate();
        var models = new AgentModelStore(options, state, new AgentContainerInspector(runner, options), mutations);
        using var client = new HttpClient(new DownloadHttpHandler(filePath));
        var factory = Substitute.For<IHttpClientFactory>(); factory.CreateClient(Arg.Any<string>()).Returns(client);
        using var manager = new AgentDownloadManager(factory, options, state, models, NullLogger<AgentDownloadManager>.Instance, mutations);
        try { await manager.StartAsync(default); await exercise(manager, state, models, root); }
        finally { await manager.StopAsync(default); SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
