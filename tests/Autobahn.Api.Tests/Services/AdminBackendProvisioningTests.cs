using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Autobahn.Api.Contracts;
using Autobahn.Api.Services;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;
using Autobahn.Application.Providers;

namespace Autobahn.Api.Tests.Services;

/// <summary>
/// Writes to one backend of a model, and what a whole-model write in the one-url shape does to a
/// model that has several.
/// </summary>
public sealed class AdminBackendProvisioningTests
{
    private const string ApiKey = "sk-upstream-0123456789abcdef";

    [Fact]
    public async Task AddBackendAsync_WithApiKey_StoresItUnderTheBackend_AndRegistersTheReference()
    {
        var (service, commands, secrets) = Create(TwoBackendModel());
        commands.AddBackendAsync("m1", Arg.Any<ModelBackendConfig>(), Arg.Any<CancellationToken>())
            .Returns(RegistryMutationResult.Ok("added"));

        var result = await service.AddBackendAsync(
            "m1",
            new AdminBackendWriteRequest
            {
                Backend = new ModelBackendConfig { Name = "c", Url = " http://c:8000 ", MaxContextLength = 4096 },
                ApiKey = ApiKey,
            },
            AdminActor.Anonymous);

        result.Success.Should().BeTrue(result.Message);
        secrets[UpstreamSecretRefs.StoreKeyForBackend("m1", "c")].Should().Be(ApiKey);
        secrets.Should().NotContainKey("m1", "the model's own slot belongs to its default backend");
        await commands.Received(1).AddBackendAsync(
            "m1",
            Arg.Is<ModelBackendConfig>(b =>
                b.Name == "c" &&
                b.Url == "http://c:8000" &&
                b.UpstreamAuth!.SecretRef == "file:model:m1:backend:c"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddBackendAsync_RefusedByTheRegistry_TakesTheStoredKeyBackOut()
    {
        var (service, commands, secrets) = Create(TwoBackendModel());
        commands.AddBackendAsync("m1", Arg.Any<ModelBackendConfig>(), Arg.Any<CancellationToken>())
            .Returns(RegistryMutationResult.Fail("taken", 409));

        var result = await service.AddBackendAsync(
            "m1",
            new AdminBackendWriteRequest { Backend = new ModelBackendConfig { Name = "c", Url = "http://c" }, ApiKey = ApiKey },
            AdminActor.Anonymous);

        result.SuggestedStatusCode.Should().Be(409);
        secrets.Should().BeEmpty();
    }

    [Fact]
    public async Task AddBackendAsync_UnknownModel_Returns404_AndStoresNothing()
    {
        var (service, commands, secrets) = Create(TwoBackendModel());

        var result = await service.AddBackendAsync(
            "missing",
            new AdminBackendWriteRequest { Backend = new ModelBackendConfig { Name = "c", Url = "http://c" }, ApiKey = ApiKey },
            AdminActor.Anonymous);

        result.SuggestedStatusCode.Should().Be(404);
        secrets.Should().BeEmpty();
        await commands.DidNotReceiveWithAnyArgs().AddBackendAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("a/b")]
    public async Task AddBackendAsync_BadName_IsRefused(string name)
    {
        var (service, _, _) = Create(TwoBackendModel());

        var result = await service.AddBackendAsync(
            "m1",
            new AdminBackendWriteRequest { Backend = new ModelBackendConfig { Name = name, Url = "http://c" } },
            AdminActor.Anonymous);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("name");
    }

    /// <summary>
    /// A rollout that only moves a backend to a new context length must not have to resend its
    /// address and its credential to keep them.
    /// </summary>
    [Fact]
    public async Task UpdateBackendAsync_BodyWithoutUrlOrCredential_KeepsBoth()
    {
        var model = TwoBackendModel();
        model.Backends![1].UpstreamAuth = new UpstreamAuthConfig { SecretRef = UpstreamSecretRefs.ForBackend("m1", "b") };
        var (service, commands, secrets) = Create(model);
        secrets[UpstreamSecretRefs.StoreKeyForBackend("m1", "b")] = "existing";
        commands.UpdateBackendAsync("m1", "b", Arg.Any<ModelBackendConfig>(), Arg.Any<CancellationToken>())
            .Returns(RegistryMutationResult.Ok("updated"));

        var result = await service.UpdateBackendAsync(
            "m1",
            "B",
            new AdminBackendWriteRequest { Backend = new ModelBackendConfig { MaxContextLength = 500_000 } },
            AdminActor.Anonymous);

        result.Success.Should().BeTrue(result.Message);
        await commands.Received(1).UpdateBackendAsync(
            "m1",
            "b",
            Arg.Is<ModelBackendConfig>(b =>
                b.Url == "http://b" &&
                b.MaxContextLength == 500_000 &&
                b.UpstreamAuth!.SecretRef == "file:model:m1:backend:b"),
            Arg.Any<CancellationToken>());
        secrets[UpstreamSecretRefs.StoreKeyForBackend("m1", "b")].Should().Be("existing");
    }

    [Fact]
    public async Task UpdateBackendAsync_ClearApiKey_RemovesTheReferenceAndTheStoredKey()
    {
        var model = TwoBackendModel();
        model.Backends![1].UpstreamAuth = new UpstreamAuthConfig { SecretRef = UpstreamSecretRefs.ForBackend("m1", "b") };
        var (service, commands, secrets) = Create(model);
        secrets[UpstreamSecretRefs.StoreKeyForBackend("m1", "b")] = "existing";
        commands.UpdateBackendAsync("m1", "b", Arg.Any<ModelBackendConfig>(), Arg.Any<CancellationToken>())
            .Returns(RegistryMutationResult.Ok("updated"));

        var result = await service.UpdateBackendAsync(
            "m1", "b", new AdminBackendWriteRequest { ClearApiKey = true }, AdminActor.Anonymous);

        result.Success.Should().BeTrue(result.Message);
        secrets.Should().BeEmpty();
        await commands.Received(1).UpdateBackendAsync(
            "m1", "b", Arg.Is<ModelBackendConfig>(b => b.UpstreamAuth == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateBackendAsync_UnknownBackend_Returns404()
    {
        var (service, _, _) = Create(TwoBackendModel());

        var result = await service.UpdateBackendAsync(
            "m1", "nope", new AdminBackendWriteRequest(), AdminActor.Anonymous);

        result.SuggestedStatusCode.Should().Be(404);
    }

    /// <summary>
    /// The console's edit of a model sends one url and may send one key. For a model with several
    /// backends the key has no backend to belong to, and guessing one would overwrite a live
    /// credential.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_OneUrlBodyWithApiKey_ForAModelWithSeveralBackends_IsRefusedBeforeAnythingIsWritten()
    {
        var (service, commands, secrets) = Create(TwoBackendModel());

        var result = await service.UpdateAsync(
            "m1",
            new AdminModelWriteRequest { Model = new ModelConfig { Id = "m1", Url = "http://a" }, ApiKey = ApiKey },
            AdminActor.Anonymous);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("2 backends");
        secrets.Should().BeEmpty();
        await commands.DidNotReceiveWithAnyArgs().UpdateModelAsync(default!, default!, default);
    }

    [Fact]
    public async Task UpdateAsync_OneUrlBody_ForAModelWithSeveralBackends_SendsTheListOnUnchanged()
    {
        var (service, commands, _) = Create(TwoBackendModel());
        commands.UpdateModelAsync("m1", Arg.Any<ModelConfig>(), Arg.Any<CancellationToken>())
            .Returns(RegistryMutationResult.Ok("updated"));

        var result = await service.UpdateAsync(
            "m1",
            new AdminModelWriteRequest
            {
                Model = new ModelConfig { Id = "m1", Url = "http://a", MaxContextLength = 8192, Aliases = ["alias"] },
            },
            AdminActor.Anonymous);

        result.Success.Should().BeTrue(result.Message);
        await commands.Received(1).UpdateModelAsync(
            "m1",
            Arg.Is<ModelConfig>(m =>
                m.Aliases.Contains("alias") &&
                m.Backends!.Count == 2 &&
                m.Backends[0].MaxContextLength == 262_144 &&
                m.Backends[1].Url == "http://b"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_WithBackendList_GivesTheApiKeyToTheFirstBackend()
    {
        var (service, commands, secrets) = Create();
        commands.AddModelAsync(Arg.Any<ModelConfig>(), Arg.Any<CancellationToken>())
            .Returns(RegistryMutationResult.Ok("added"));

        var result = await service.AddAsync(
            new AdminModelWriteRequest { Model = TwoBackendModel("new"), ApiKey = ApiKey },
            AdminActor.Anonymous);

        result.Success.Should().BeTrue(result.Message);
        secrets[UpstreamSecretRefs.StoreKeyForBackend("new", "a")].Should().Be(ApiKey);
        await commands.Received(1).AddModelAsync(
            Arg.Is<ModelConfig>(m =>
                m.Backends![0].UpstreamAuth!.SecretRef == "file:model:new:backend:a" &&
                m.Backends[1].UpstreamAuth == null &&
                m.Url == "http://a"),
            Arg.Any<CancellationToken>());
    }

    private static ModelConfig TwoBackendModel(string id = "m1") =>
        ModelBackends.Normalize(new ModelConfig
        {
            Id = id,
            Backends =
            [
                new ModelBackendConfig { Name = "a", Url = "http://a", MaxContextLength = 262_144 },
                new ModelBackendConfig { Name = "b", Url = "http://b", MaxContextLength = 1_000_000 },
            ],
        });

    private static (AdminModelProvisioningService Service, IControlPlaneCommands Commands, Dictionary<string, string> Secrets)
        Create(params ModelConfig[] models)
    {
        var commands = Substitute.For<IControlPlaneCommands>();
        commands.ListModels().Returns(models);

        var pricing = Substitute.For<IRateCardAdminService>();
        pricing.GetPricingByModelAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ModelPricing>());

        var services = new ServiceCollection();
        services.AddSingleton(pricing);
        var provider = services.BuildServiceProvider();

        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var service = new AdminModelProvisioningService(
            commands,
            new DictionarySecretStore(secrets),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new UpstreamEnvVarPolicy(),
            Substitute.For<IAuditLogger>());
        return (service, commands, secrets);
    }

    private sealed class DictionarySecretStore(Dictionary<string, string> secrets) : IUpstreamSecretStore
    {
        public bool TryGet(string modelId, out string? secret) => secrets.TryGetValue(modelId, out secret);

        public Task PutAsync(string modelId, string secret, CancellationToken cancellationToken = default)
        {
            secrets[modelId] = secret;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string modelId, CancellationToken cancellationToken = default)
        {
            secrets.Remove(modelId);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string modelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(secrets.ContainsKey(modelId));

        public Task<IReadOnlySet<string>> ListExistingAsync(
            IEnumerable<string> modelIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(
                modelIds.Where(secrets.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
}
