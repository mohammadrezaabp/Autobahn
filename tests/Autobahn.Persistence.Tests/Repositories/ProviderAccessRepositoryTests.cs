using Autobahn.Domain.Identity;
using Autobahn.Persistence.Repositories;
using Autobahn.Persistence.Tests.Infrastructure;

namespace Autobahn.Persistence.Tests.Repositories;

public sealed class ProviderAccessRepositoryTests
{
    [Fact]
    public async Task SetAsync_WritesOneRowPerLevel_AndReportsWhatItReplaced()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(SetAsync_WritesOneRowPerLevel_AndReportsWhatItReplaced));
        var (tenantId, keyId) = await SeedAsync(db, "t1");
        var sut = new ProviderAccessRepository(db);

        (await sut.SetAsync(tenantId, null, "openrouter", true)).Should().BeFalse("it had never been set");
        (await sut.SetAsync(tenantId, keyId, "openrouter", true)).Should().BeFalse();
        (await sut.SetAsync(tenantId, null, "openrouter", false)).Should().BeTrue();

        var rows = await sut.ListByTenantAsync(tenantId);
        rows.Should().HaveCount(2);
        rows.Single(r => r.ApiKeyId is null).IsOpen.Should().BeFalse();
        rows.Single(r => r.ApiKeyId == keyId).IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task ListByTenantAsync_LeavesOutAnotherTenantsSwitches()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(ListByTenantAsync_LeavesOutAnotherTenantsSwitches));
        var (first, _) = await SeedAsync(db, "first");
        var (second, _) = await SeedAsync(db, "second");
        var sut = new ProviderAccessRepository(db);

        await sut.SetAsync(first, null, "openrouter", true);

        (await sut.ListByTenantAsync(second)).Should().BeEmpty();
    }

    /// <summary>
    /// The tenant that opened a key is not the one paying after the key moves, so the switch must
    /// not follow it — and must not linger in the old tenant's count of opened keys.
    /// </summary>
    [Fact]
    public async Task AKeyMovedToAnotherTenant_StartsClosedThere()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(AKeyMovedToAnotherTenant_StartsClosedThere));
        var (source, keyId) = await SeedAsync(db, "source");
        var (target, _) = await SeedAsync(db, "target");
        var sut = new ProviderAccessRepository(db);
        await sut.SetAsync(source, keyId, "openrouter", true);

        await new ApiKeyRepository(db).MoveToTenantAsync(keyId, target);

        (await sut.ListByTenantAsync(source)).Should().BeEmpty();
        (await sut.ListByTenantAsync(target)).Should().BeEmpty();
    }

    private static async Task<(Guid TenantId, Guid KeyId)> SeedAsync(GatewayDbContext db, string slug)
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = Guid.NewGuid();
        var keyId = Guid.NewGuid();
        await new TenantRepository(db).CreateAsync(new TenantRecord(tenantId, slug, slug, null, null, true, now, now));
        await new ApiKeyRepository(db).CreateAsync(
            new ApiKeyRecord(keyId, tenantId, "hash-" + slug, "sk-" + slug, ApiKeyRole.Inference, [], null, null, now, null));
        return (tenantId, keyId);
    }
}
