using Autobahn.Domain.Billing;
using Autobahn.Persistence.Repositories;
using Autobahn.Persistence.Tests.Infrastructure;

namespace Autobahn.Persistence.Tests.Repositories;

public sealed class ProviderSpendRepositoryTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Key = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateOnly Day = new(2026, 10, 13);

    [Fact]
    public async Task IncrementAsync_AddsToTheSameBucket_AcrossCallsAndWithinOne()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(IncrementAsync_AddsToTheSameBucket_AcrossCallsAndWithinOne));
        var sut = new ProviderSpendRepository(db);

        await sut.IncrementAsync(
        [
            Delta(Day, "qwen/a", 0.25m, 1),
            // The registry folds case, so the two spellings are one model.
            Delta(Day, "QWEN/A", 0.5m, 2),
        ]);
        await sut.IncrementAsync([Delta(Day, "qwen/a", 0.0000001m, 1)]);

        var rows = await sut.ListAsync(Day, Day);
        var row = rows.Should().ContainSingle().Subject;
        row.Cost.Should().Be(0.7500001m, "a request that costs a ten-millionth of a dollar still counts");
        row.RequestCount.Should().Be(4);
        row.TenantId.Should().Be(Tenant);
        row.ApiKeyId.Should().Be(Key);
    }

    [Fact]
    public async Task ListAsync_NarrowsToTheDaysTheProviderAndTheTenant()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(ListAsync_NarrowsToTheDaysTheProviderAndTheTenant));
        var sut = new ProviderSpendRepository(db);
        var otherTenant = Guid.NewGuid();

        await sut.IncrementAsync(
        [
            Delta(Day, "qwen/a", 1m, 1),
            Delta(Day.AddDays(-40), "qwen/a", 2m, 1),
            Delta(Day, "deepseek-chat", 4m, 1) with { Provider = "deepseek" },
            Delta(Day, "qwen/a", 8m, 1) with { TenantId = otherTenant },
            Delta(Day, "qwen/a", 16m, 1) with { TenantId = null, ApiKeyId = null },
        ]);

        (await sut.ListAsync(Day.AddDays(-1), Day, "openrouter", Tenant)).Sum(r => r.Cost).Should().Be(1m);
        (await sut.ListAsync(Day.AddDays(-60), Day, "openrouter", Tenant)).Sum(r => r.Cost).Should().Be(3m);
        (await sut.ListAsync(Day, Day, "deepseek")).Sum(r => r.Cost).Should().Be(4m);
        (await sut.ListAsync(Day, Day, "openrouter")).Sum(r => r.Cost).Should().Be(25m);

        // Keyless traffic comes back without ids, not as an all-zero one.
        (await sut.ListAsync(Day, Day, "openrouter")).Should().Contain(r => r.TenantId == null && r.ApiKeyId == null);
    }

    [Fact]
    public async Task ProviderSettings_AreUpsertedPerProvider()
    {
        await using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(ProviderSettings_AreUpsertedPerProvider));
        var sut = new ProviderSettingsRepository(db);

        await sut.UpsertAsync(new ProviderSettingsRecord("openrouter", 20m, true));
        await sut.UpsertAsync(new ProviderSettingsRecord("openrouter", null, false));
        await sut.UpsertAsync(new ProviderSettingsRecord("deepseek", 5m, false));

        var all = await sut.ListAsync();
        all.Should().HaveCount(2);
        all.Single(s => s.Provider == "openrouter").Should().Be(new ProviderSettingsRecord("openrouter", null, false));
    }

    private static ProviderSpendDelta Delta(DateOnly day, string model, decimal cost, int requests) =>
        new(day, Tenant, Key, "openrouter", model, cost, requests);
}
