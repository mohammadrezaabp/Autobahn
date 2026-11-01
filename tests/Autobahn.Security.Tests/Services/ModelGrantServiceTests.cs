using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;
using Autobahn.Security.Configuration;
using Autobahn.Security.Services;

namespace Autobahn.Security.Tests.Services;

public sealed class ModelGrantServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid KeyId = Guid.NewGuid();

    [Fact]
    public async Task IsModelAllowed_CachesGrantsAfterFirstLoad()
    {
        var tenantRepo = new BlockingTenantRepo();
        var keyRepo = Substitute.For<IApiKeyModelGrantRepository>();
        keyRepo.ListByApiKeyAsync(KeyId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ApiKeyModelGrantRecord>>([Allow("gpt-4o")]));
        var service = CreateService(tenantRepo, keyRepo);
        tenantRepo.Gate.SetResult();

        (await service.IsModelAllowedAsync(TenantId, KeyId, "gpt-4o")).Should().BeTrue();
        (await service.IsModelAllowedAsync(TenantId, KeyId, "gpt-4o")).Should().BeTrue();

        tenantRepo.Loads.Should().Be(1);
    }

    /// <summary>
    /// A request that missed the cache reads the OLD grants; while that read is in flight an admin
    /// replaces the tenant's grants and calls Invalidate. Removing the cache entry alone would let
    /// the in-flight load then cache the pre-revocation list for the whole TTL, so the "in-process
    /// invalidation is immediate" promise would be broken exactly for the hot keys.
    /// </summary>
    [Fact]
    public async Task InvalidateTenantGrants_DuringInFlightLoad_PreventsStaleResultFromBeingCached()
    {
        var tenantRepo = new BlockingTenantRepo();
        var keyRepo = Substitute.For<IApiKeyModelGrantRepository>();
        keyRepo.ListByApiKeyAsync(KeyId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ApiKeyModelGrantRecord>>([Allow("*")]));
        var service = CreateService(tenantRepo, keyRepo);

        // First read (old grants: tenant may use gpt-4o) starts and blocks inside the repository.
        var raced = service.IsModelAllowedAsync(TenantId, KeyId, "gpt-4o");
        await tenantRepo.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Admin revokes: the repository now denies gpt-4o and the service is told to invalidate.
        tenantRepo.Current = [new ModelGrantRecord(Guid.NewGuid(), TenantId, "gpt-4o", GrantEffect.Deny)];
        service.InvalidateTenantGrants(TenantId);
        tenantRepo.Gate.SetResult();

        // The raced caller may see either answer; what matters is what gets cached afterwards.
        await raced;

        (await service.IsModelAllowedAsync(TenantId, KeyId, "gpt-4o"))
            .Should().BeFalse("the load that raced the invalidation must not have populated the cache");
        tenantRepo.Loads.Should().Be(2);
    }

    private static ModelGrantService CreateService(IModelGrantRepository tenantRepo, IApiKeyModelGrantRepository keyRepo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantRepo);
        services.AddSingleton(keyRepo);
        var provider = services.BuildServiceProvider();
        return new ModelGrantService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IModelRegistry>(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GatewaySecurityOptions { CacheTtlMinutes = 5 }));
    }

    /// <summary>
    /// The switches are cached like the lists, so opening a key has to drop them: otherwise the key
    /// stays refused for a whole TTL after an operator opened it.
    /// </summary>
    [Fact]
    public async Task OpenRouterModel_IsRefusedUntilBothSwitchesAreOpen_AndInvalidationIsImmediate()
    {
        const string model = "qwen/qwen3.8-27b";
        var registry = Substitute.For<IModelRegistry>();
        registry.TryGetModel(model, out Arg.Any<ModelConfig?>())
            .Returns(call =>
            {
                call[1] = new ModelConfig { Id = model, Url = "https://openrouter.ai/api" };
                return true;
            });

        var tenantRepo = Substitute.For<IModelGrantRepository>();
        tenantRepo.ListByTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ModelGrantRecord>>([]));
        var keyRepo = Substitute.For<IApiKeyModelGrantRepository>();
        keyRepo.ListByApiKeyAsync(KeyId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ApiKeyModelGrantRecord>>([Allow(model)]));

        IReadOnlyList<ProviderAccessRecord> switches = [];
        var accessRepo = Substitute.For<IProviderAccessRepository>();
        accessRepo.ListByTenantAsync(TenantId, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(switches));

        var services = new ServiceCollection();
        services.AddSingleton(tenantRepo);
        services.AddSingleton(keyRepo);
        services.AddSingleton(accessRepo);
        var service = new ModelGrantService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            registry,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GatewaySecurityOptions { CacheTtlMinutes = 5 }));

        (await service.EvaluateAsync(TenantId, KeyId, model)).Refusal
            .Should().Be(ModelAccessRefusal.ProviderClosedForTenant);

        switches =
        [
            new ProviderAccessRecord(Guid.NewGuid(), TenantId, null, ModelProviders.OpenRouter, true),
            new ProviderAccessRecord(Guid.NewGuid(), TenantId, KeyId, ModelProviders.OpenRouter, true),
        ];
        (await service.IsModelAllowedAsync(TenantId, KeyId, model))
            .Should().BeFalse("the closed answer is cached until it is invalidated");

        service.InvalidateProviderAccess(TenantId);

        (await service.IsModelAllowedAsync(TenantId, KeyId, model)).Should().BeTrue();
    }

    private static ApiKeyModelGrantRecord Allow(string pattern) =>
        new(Guid.NewGuid(), KeyId, pattern, GrantEffect.Allow);

    private sealed class BlockingTenantRepo : IModelGrantRepository
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<ModelGrantRecord> Current { get; set; } =
            [new ModelGrantRecord(Guid.NewGuid(), TenantId, "gpt-4o", GrantEffect.Allow)];

        public int Loads { get; private set; }

        public async Task<IReadOnlyList<ModelGrantRecord>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            Loads++;
            var snapshot = Current;
            Entered.TrySetResult();
            await Gate.Task.WaitAsync(cancellationToken);
            return snapshot;
        }

        public Task<ModelGrantRecord> AddAsync(ModelGrantRecord grant, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ReplaceForTenantAsync(Guid tenantId, IReadOnlyList<string> modelPatterns, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
