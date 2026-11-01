using NSubstitute;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;
using Autobahn.Observability.ControlPlane;

namespace Autobahn.Observability.Tests.ControlPlane;

public sealed class ControlPlaneCommandsTests
{
    [Fact]
    public async Task ReloadConfigAsync_DelegatesToConfigReload()
    {
        var reload = Substitute.For<IConfigReload>();
        reload.ReloadAsync(Arg.Any<CancellationToken>())
            .Returns(ConfigReloadResult.Success("reloaded", 1, 1, []));

        var commands = CreateCommands(configReload: reload);
        var result = await commands.ReloadConfigAsync();

        result.Status.Should().Be("success");
        await reload.Received(1).ReloadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ListModels_ReturnsRegistryModels()
    {
        var registry = Substitute.For<IModelRegistry>();
        registry.GetAllModels().Returns(
        [
            new ModelConfig { Id = "m1", Url = "http://localhost:9000", Aliases = ["alias-1"] },
        ]);

        var commands = CreateCommands(registry: registry);
        var models = commands.ListModels();

        models.Should().ContainSingle();
        models[0].Id.Should().Be("m1");
        models[0].Aliases.Should().Contain("alias-1");
    }

    [Fact]
    public void ListBackends_MapsRegistryAndHealth()
    {
        var registry = Substitute.For<IModelRegistry>();
        registry.GetAllModels().Returns(
        [
            new ModelConfig { Id = "m1", Url = "http://localhost:9000" },
        ]);

        var health = Substitute.For<IBackendHealthStore>();
        health.IsBackendHealthy("m1").Returns(true);
        health.IsBackendHealthy("m1", Arg.Any<string>()).Returns(true);

        var commands = CreateCommands(registry: registry, healthStore: health);
        var backends = commands.ListBackends();

        backends.Should().ContainSingle();
        backends[0].ModelId.Should().Be("m1");
        backends[0].IsHealthy.Should().BeTrue();
    }

    /// <summary>
    /// One row per backend, each with its own health and its own count of requests in flight —
    /// the count is what tells a rollout that a stopped backend has emptied.
    /// </summary>
    [Fact]
    public void ListBackends_SeveralBackends_GivesEachItsOwnRow()
    {
        var registry = Substitute.For<IModelRegistry>();
        registry.GetAllModels().Returns(
        [
            new ModelConfig
            {
                Id = "m1",
                Backends =
                [
                    new ModelBackendConfig { Name = "a", Url = "http://a", MaxContextLength = 262_144 },
                    new ModelBackendConfig
                    {
                        Name = "b", Url = "http://b", State = ModelRouteStates.Stopped,
                        Labels = new Dictionary<string, string> { ["managed-by"] = "fleet" },
                    },
                ],
            },
        ]);

        var health = Substitute.For<IBackendHealthStore>();
        health.IsBackendHealthy("m1", Arg.Any<string>()).Returns(true);
        var inFlight = Substitute.For<IBackendInFlightSource>();
        inFlight.GetInFlight("m1", "b").Returns(3);

        var commands = new ControlPlaneCommands(
            Substitute.For<IConfigReload>(),
            registry,
            health,
            Substitute.For<IAdminSummaryReader>(),
            Substitute.For<IRecentRequestStore>(),
            Substitute.For<IModelRegistryWriter>(),
            inFlight);
        var backends = commands.ListBackends();

        backends.Select(b => (b.ModelId, b.Backend, b.Url)).Should().Equal(
            ("m1", "a", "http://a"),
            ("m1", "b", "http://b"));
        backends[0].MaxContextLength.Should().Be(262_144);
        backends[0].IsHealthy.Should().BeTrue();
        backends[1].State.Should().Be(ModelRouteStates.Stopped);
        backends[1].IsHealthy.Should().BeFalse("a stopped backend is not probed, so it is not reported healthy");
        backends[1].InFlight.Should().Be(3);
        backends[1].Labels.Should().ContainKey("managed-by");
    }

    [Fact]
    public void GetConfigStatus_DelegatesToConfigReload()
    {
        var reload = Substitute.For<IConfigReload>();
        reload.GetStatus().Returns(new ConfigStatusResponse { ModelCount = 2 });

        var commands = CreateCommands(configReload: reload);

        commands.GetConfigStatus().ModelCount.Should().Be(2);
        reload.Received(1).GetStatus();
    }

    [Fact]
    public void GetSummary_DelegatesToSummaryReader()
    {
        var summary = Substitute.For<IAdminSummaryReader>();
        summary.GetSnapshot().Returns(new AdminSummarySnapshot
        {
            Uptime = "1m",
            TotalInferenceRequests = 9,
        });

        var commands = CreateCommands(summaryReader: summary);

        commands.GetSummary().TotalInferenceRequests.Should().Be(9);
    }

    [Fact]
    public void ListRecentRequests_DelegatesToRecentStore()
    {
        var recent = Substitute.For<IRecentRequestStore>();
        recent.GetRecent(5).Returns(
        [
            new RecentRequestEntry
            {
                RequestId = "r1",
                Method = "GET",
                Path = "/v1/models",
                StatusCode = 200,
            },
        ]);

        var commands = CreateCommands(recentRequestStore: recent);

        commands.ListRecentRequests(5).Should().ContainSingle(e => e.RequestId == "r1");
        recent.Received(1).GetRecent(5);
    }

    [Fact]
    public async Task AddUpdateRemoveModel_DelegateToWriter()
    {
        var writer = Substitute.For<IModelRegistryWriter>();
        var model = new ModelConfig { Id = "m1", Url = "http://localhost:1" };
        writer.AddModelAsync(model, Arg.Any<CancellationToken>())
            .Returns(RegistryMutationResult.Ok("added"));
        writer.UpdateModelAsync("m1", model, Arg.Any<CancellationToken>())
            .Returns(RegistryMutationResult.Ok("updated"));
        writer.RemoveModelAsync("m1", Arg.Any<CancellationToken>())
            .Returns(RegistryMutationResult.Ok("removed"));

        var commands = CreateCommands(registryWriter: writer);

        (await commands.AddModelAsync(model)).Success.Should().BeTrue();
        (await commands.UpdateModelAsync("m1", model)).Success.Should().BeTrue();
        (await commands.RemoveModelAsync("m1")).Success.Should().BeTrue();
    }

    private static ControlPlaneCommands CreateCommands(
        IConfigReload? configReload = null,
        IModelRegistry? registry = null,
        IBackendHealthStore? healthStore = null,
        IAdminSummaryReader? summaryReader = null,
        IRecentRequestStore? recentRequestStore = null,
        IModelRegistryWriter? registryWriter = null)
    {
        configReload ??= Substitute.For<IConfigReload>();
        registry ??= Substitute.For<IModelRegistry>();
        healthStore ??= Substitute.For<IBackendHealthStore>();
        summaryReader ??= Substitute.For<IAdminSummaryReader>();
        recentRequestStore ??= Substitute.For<IRecentRequestStore>();
        registryWriter ??= Substitute.For<IModelRegistryWriter>();

        return new ControlPlaneCommands(
            configReload,
            registry,
            healthStore,
            summaryReader,
            recentRequestStore,
            registryWriter);
    }
}
