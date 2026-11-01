using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Domain.Models;
using Autobahn.Registry.Hosting;
using Autobahn.Registry.Services;

namespace Autobahn.Registry.Tests.Hosting;

public sealed class UpstreamSecretVerificationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_WithUndecryptableSecret_CompletesWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Autobahn-secrets-{Guid.NewGuid():N}.enc");
        try
        {
            var writer = CreateStore(path, pepper: "original-pepper");
            await writer.PutAsync("model-a", "sk-secret");

            var reader = CreateStore(path, pepper: "rotated-pepper");
            var service = new UpstreamSecretVerificationHostedService(
                reader,
                RegistryWith("model-a"),
                NullLogger<UpstreamSecretVerificationHostedService>.Instance);

            var act = async () => await service.StartAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();

            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task StartAsync_WithValidSecrets_CompletesWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Autobahn-secrets-{Guid.NewGuid():N}.enc");
        try
        {
            var store = CreateStore(path);
            await store.PutAsync("model-a", "sk-secret");
            var service = new UpstreamSecretVerificationHostedService(
                store,
                RegistryWith("model-a"),
                NullLogger<UpstreamSecretVerificationHostedService>.Instance);

            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static IModelRegistry RegistryWith(params string[] modelIds)
    {
        var registry = Substitute.For<IModelRegistry>();
        registry.IsLoaded.Returns(true);
        registry.GetAllModels().Returns(modelIds
            .Select(id => new ModelConfig
            {
                Id = id,
                Url = "http://upstream.test",
                UpstreamAuth = new UpstreamAuthConfig { Type = "bearer", SecretRef = UpstreamSecretRefs.ForModel(id) },
            })
            .ToList());
        return registry;
    }

    private static FileUpstreamSecretStore CreateStore(string secretsPath, string pepper = "test-pepper")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:Security:KeyPepper"] = pepper,
            })
            .Build();

        return new FileUpstreamSecretStore(
            Options.Create(new GatewayOptions
            {
                ModelsConfigPath = Path.Combine(Path.GetTempPath(), "unused-models.json"),
                UpstreamSecretsPath = secretsPath,
            }),
            config,
            new TestHostEnvironment(),
            NullLogger<FileUpstreamSecretStore>.Instance);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
