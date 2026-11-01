using Autobahn.Domain.Identity;
using Autobahn.Persistence.Repositories;
using Autobahn.Persistence.Tests.Infrastructure;

namespace Autobahn.Persistence.Tests.Repositories;

public sealed class ApiKeyModelGrantRepositoryTests
{
    [Fact]
    public async Task ReplaceForApiKeyAsync_ReplacesAllowlist()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(ReplaceForApiKeyAsync_ReplacesAllowlist));
        var tenantRepo = new TenantRepository(db);
        var apiKeyRepo = new ApiKeyRepository(db);
        var sut = new ApiKeyModelGrantRepository(db);
        var now = DateTimeOffset.UtcNow;
        var tenantId = Guid.NewGuid();
        var keyId = Guid.NewGuid();

        await tenantRepo.CreateAsync(new TenantRecord(tenantId, "t1", "Tenant", null, null, true, now, now));
        await apiKeyRepo.CreateAsync(
            new ApiKeyRecord(keyId, tenantId, "hash", "sk-prefix", ApiKeyRole.Inference, [], null, null, now, null));

        await sut.ReplaceForApiKeyAsync(keyId, ["model-a", "model-b"]);

        var grants = await sut.ListByApiKeyAsync(keyId);
        grants.Select(g => g.ModelPattern).Should().BeEquivalentTo(["model-a", "model-b"]);

        await sut.ReplaceForApiKeyAsync(keyId, ["model-c"]);
        grants = await sut.ListByApiKeyAsync(keyId);
        grants.Should().ContainSingle(g => g.ModelPattern == "model-c");
    }

    [Fact]
    public async Task ReplaceForApiKeyAsync_KeepsEachEntrysEffect()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(ReplaceForApiKeyAsync_KeepsEachEntrysEffect));
        var now = DateTimeOffset.UtcNow;
        var tenantId = Guid.NewGuid();
        var keyId = Guid.NewGuid();
        await new TenantRepository(db).CreateAsync(new TenantRecord(tenantId, "t1", "Tenant", null, null, true, now, now));
        await new ApiKeyRepository(db).CreateAsync(
            new ApiKeyRecord(keyId, tenantId, "hash", "sk-prefix", ApiKeyRole.Inference, [], null, null, now, null));
        var sut = new ApiKeyModelGrantRepository(db);

        await sut.ReplaceForApiKeyAsync(
            keyId,
            [("qwen/*", GrantEffect.Allow), ("qwen/big", GrantEffect.Deny), ("QWEN/BIG", GrantEffect.Allow)]);

        var grants = await sut.ListByApiKeyAsync(keyId);
        grants.Should().HaveCount(2);
        grants.Single(g => g.ModelPattern == "qwen/*").Effect.Should().Be(GrantEffect.Allow);
        grants.Single(g => g.ModelPattern != "qwen/*").Effect.Should().Be(GrantEffect.Deny, "a deny is never lost to an allow of the same model");
    }
}
