using System.Text.Json;
using System.Text.Json.Nodes;
using Autobahn.Application.Fleet;
using Autobahn.Domain.Fleet;
using Autobahn.Infrastructure.Fleet;
using Microsoft.Data.Sqlite;

namespace Autobahn.Fleet.Tests;

public sealed class FleetControlServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "autobahn-fleet-test-" + Guid.NewGuid().ToString("N"));
    private readonly IFleetAgentClient _agents = Substitute.For<IFleetAgentClient>();
    private readonly IGitOpsClient _git = Substitute.For<IGitOpsClient>();
    private readonly IManagedRouteSynchronizer _routes = Substitute.For<IManagedRouteSynchronizer>();
    private readonly SqliteFleetStateStore _state;
    private readonly FleetControlService _service;
    private static ServerSpecification Spec => new() { Name = "chat", Model = "example/chat", Gpus = [0], Port = 8000, Image = "vllm/vllm-openai:v1", ServedName = "chat" };

    public FleetControlServiceTests()
    {
        _state = new SqliteFleetStateStore(_directory);
        _agents.Nodes.Returns([new FleetNode { Id = "node-a", Address = "http://localhost:9455", InferenceAddress = "http://localhost", Token = new string('x', 32) }]);
        _agents.CallAsync(Arg.Any<string>(), Arg.Any<HttpMethod>(), Arg.Any<string>(), Arg.Any<JsonNode?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<JsonNode>(new JsonObject { ["action"] = "create", ["imagePresent"] = true, ["warnings"] = new JsonArray() }));
        _service = new FleetControlService(_agents, _state, _git, _routes);
    }

    private static JsonNode Change() => new JsonObject { ["add"] = new JsonArray(JsonSerializer.SerializeToNode(Spec, FleetControlService.Json)), ["remove"] = new JsonArray() };

    [Fact]
    public async Task ProposingDoesNotApplyOrPublishRoutes()
    {
        var proposed = await _service.ProposeAsync("node-a", Change(), "operator", default);
        proposed["id"].Should().NotBeNull();
        await _routes.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!, default);
        var calls = _agents.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IFleetAgentClient.CallAsync)).ToArray();
        calls.Should().HaveCount(1);
        ((JsonNode)calls[0].GetArguments()[3]!)["dryRun"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ApplyingAReviewedChangePublishesItsRouteAndCannotRepeat()
    {
        var proposed = await _service.ProposeAsync("node-a", Change(), "operator", default);
        var id = proposed["id"]!.ToString();
        var applied = await _service.ApplyAsync(id, "operator", default);
        applied["status"]!.ToString().Should().Be("applied");
        (await _service.InventoryAsync(default))["servers"]!.AsArray().Should().ContainSingle();
        await _routes.Received(1).PublishAsync("node-a", Arg.Is<ServerSpecification>(x => x.Name == "chat"), Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<FleetException>(() => _service.ApplyAsync(id, "operator", default));
    }

    [Fact]
    public async Task AChangedInventoryRefusesAnOldProposalBeforeHostMutation()
    {
        var proposed = await _service.ProposeAsync("node-a", Change(), "operator", default);
        await _state.WriteAsync("inventory", "other", new JsonObject { ["name"] = "other", ["host"] = "node-b" });
        _agents.ClearReceivedCalls();
        var error = await Assert.ThrowsAsync<FleetException>(() => _service.ApplyAsync(proposed["id"]!.ToString(), "operator", default));
        error.Code.Should().Be("stale_inventory");
        await _agents.DidNotReceiveWithAnyArgs().CallAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task AConflictingPlanCannotBeProposed()
    {
        var other = JsonSerializer.SerializeToNode(Spec with { Name = "other" }, FleetControlService.Json)!; other["host"] = "node-a";
        await _state.WriteAsync("inventory", "other", other);
        var plan = await _service.PlanAsync("node-a", Change(), default);
        plan["ok"]!.GetValue<bool>().Should().BeFalse();
        await Assert.ThrowsAsync<FleetException>(() => _service.ProposeAsync("node-a", Change(), "operator", default));
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
