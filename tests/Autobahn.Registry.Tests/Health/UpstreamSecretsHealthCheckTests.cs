using Autobahn.Domain.Models;
using Autobahn.Application.Abstractions;
using NSubstitute;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Autobahn.Domain.Configuration;
using Autobahn.Registry.Health;
using Autobahn.Registry.Services;

namespace Autobahn.Registry.Tests.Health;

public sealed class UpstreamSecretsHealthCheckTests
{
    [Fact]
    public async Task CheckHealth_WithUndecryptableSecret_ReportsDegraded_ThenHealthyOnceReEntered()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Autobahn-secrets-{Guid.NewGuid():N}.enc");
        try
        {
            var writer = CreateStore(path, pepper: "original-pepper");
            await writer.PutAsync("model-a", "sk-secret");

            // Pepper rotated: the stored credential no longer decrypts.
            var store = CreateStore(path, pepper: "rotated-pepper");
            var recorder = Substitute.For<IGatewayErrorRecorder>();
            var check = new UpstreamSecretsHealthCheck(store, RegistryWith("model-a"), recorder);

            var degraded = await check.CheckHealthAsync(new HealthCheckContext());
            degraded.Status.Should().Be(HealthStatus.Degraded);
            degraded.Description.Should().Contain("1 of 1").And.Contain("KeyPepper").And.Contain("model-a");

            // Recorded once for the Errors tab, not once per probe.
            await check.CheckHealthAsync(new HealthCheckContext());
            recorder.Received(1).Record(Arg.Is<GatewayErrorRecord>(r =>
                r.Source == GatewayErrorSourceNames.Health && r.Level == "Critical" && r.EventCode == "secrets_undecryptable"));

            // The operator re-enters the key under the current pepper; the very next probe must
            // recover without a restart, which a startup-cached verdict could not do.
            await store.PutAsync("model-a", "sk-secret-again");

            var healthy = await check.CheckHealthAsync(new HealthCheckContext());
            healthy.Status.Should().Be(HealthStatus.Healthy);
            healthy.Description.Should().Contain("Verified 1");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task CheckHealth_WithNoStoredSecrets_IsHealthy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Autobahn-secrets-{Guid.NewGuid():N}.enc");
        var result = await new UpstreamSecretsHealthCheck(CreateStore(path), RegistryWith()).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_WithUndecryptableSecretNoModelPointsAt_IsHealthy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Autobahn-secrets-{Guid.NewGuid():N}.enc");
        try
        {
            await CreateStore(path, pepper: "original-pepper").PutAsync("removed-model", "sk-secret");

            // The entry outlived its model: nothing reads it, so it cannot fail a request.
            var store = CreateStore(path, pepper: "rotated-pepper");
            var recorder = Substitute.For<IGatewayErrorRecorder>();
            var check = new UpstreamSecretsHealthCheck(store, RegistryWith("model-a"), recorder);

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            result.Status.Should().Be(HealthStatus.Healthy);
            recorder.DidNotReceive().Record(Arg.Any<GatewayErrorRecord>());
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
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gateway:Security:KeyPepper"] = pepper })
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
