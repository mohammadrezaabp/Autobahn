using Autobahn.Domain.Identity;
using Autobahn.Persistence.Repositories;
using Autobahn.Persistence.Tests.Infrastructure;

namespace Autobahn.Persistence.Tests.Repositories;

public sealed class TenantRepositoryTests
{
    [Fact]
    public async Task CreateAsync_ThenGetBySlug_ReturnsTenant()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(CreateAsync_ThenGetBySlug_ReturnsTenant));
        var sut = new TenantRepository(db);
        var now = DateTimeOffset.UtcNow;
        var tenant = new TenantRecord(
            Guid.NewGuid(),
            "acme",
            "Acme Corp",
            "standard",
            "cc-1",
            true,
            now,
            now);

        await sut.CreateAsync(tenant);

        var loaded = await sut.GetBySlugAsync("acme");

        loaded.Should().NotBeNull();
        loaded!.Slug.Should().Be("acme");
        loaded.Name.Should().Be("Acme Corp");
        loaded.PlanSlug.Should().Be("standard");
    }

    [Fact]
    public async Task ListActiveAsync_ExcludesInactiveTenants()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(ListActiveAsync_ExcludesInactiveTenants));
        var sut = new TenantRepository(db);
        var now = DateTimeOffset.UtcNow;

        await sut.CreateAsync(new TenantRecord(Guid.NewGuid(), "active", "Active", null, null, true, now, now));
        await sut.CreateAsync(new TenantRecord(Guid.NewGuid(), "inactive", "Inactive", null, null, false, now, now));

        var active = await sut.ListActiveAsync();

        active.Should().ContainSingle(t => t.Slug == "active");
    }

    [Fact]
    public async Task ListAllAsync_IncludesInactiveTenants_OrderedBySlug()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(ListAllAsync_IncludesInactiveTenants_OrderedBySlug));
        var sut = new TenantRepository(db);
        var now = DateTimeOffset.UtcNow;

        await sut.CreateAsync(new TenantRecord(Guid.NewGuid(), "zeta", "Zeta", null, null, true, now, now));
        await sut.CreateAsync(new TenantRecord(Guid.NewGuid(), "alpha", "Alpha", null, null, false, now, now));

        var all = await sut.ListAllAsync();

        all.Select(t => t.Slug).Should().Equal("alpha", "zeta");
    }

    [Fact]
    public async Task UpdateAsync_ReplacesNamePlanAndCostCenter_AndLeavesTheSlug()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(UpdateAsync_ReplacesNamePlanAndCostCenter_AndLeavesTheSlug));
        var sut = new TenantRepository(db);
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var id = Guid.NewGuid();
        await sut.CreateAsync(new TenantRecord(id, "acme", "Acme", "standard", "cc-1", true, created, created));
        var updatedAt = DateTimeOffset.UtcNow;

        var updated = await sut.UpdateAsync(id, "Acme Corp", planSlug: null, costCenter: "cc-2", updatedAt);

        updated.Should().BeEquivalentTo(
            new TenantRecord(id, "acme", "Acme Corp", null, "cc-2", true, created, updatedAt));
        (await sut.GetByIdAsync(id)).Should().BeEquivalentTo(updated);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_Throws()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(UpdateAsync_UnknownId_Throws));
        var sut = new TenantRepository(db);

        var act = () => sut.UpdateAsync(Guid.NewGuid(), "X", null, null, DateTimeOffset.UtcNow);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
