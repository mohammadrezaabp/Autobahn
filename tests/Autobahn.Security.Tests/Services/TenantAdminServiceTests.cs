using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;
using Autobahn.Domain.RateLimiting;
using Autobahn.Persistence;
using Autobahn.Persistence.Entities;
using Autobahn.Persistence.Repositories;
using Autobahn.Persistence.Security;
using Autobahn.Security.Configuration;
using Autobahn.Security.Services;
using TenantRepository = Autobahn.Persistence.Repositories.TenantRepository;

namespace Autobahn.Security.Tests.Services;

public sealed class TenantAdminServiceTests
{
    private const string Pepper = "test-pepper";
    private const string OperatorSlug = "default";

    [Fact]
    public async Task CreateAsync_NormalizesSlug_AndListsTheTenantWithNoKeys()
    {
        await using var fx = await Fixture.CreateAsync();

        var created = await fx.Sut.CreateAsync(new CreateAdminTenantRequest
        {
            Slug = "  Fanus-MMT ",
            Name = " Fanus MMT ",
            CostCenter = "fanus",
        });

        created.Slug.Should().Be("fanus-mmt");
        created.Name.Should().Be("Fanus MMT");
        created.IsActive.Should().BeTrue();
        created.IsOperatorTenant.Should().BeFalse();

        var list = await fx.Sut.ListAsync();
        list.Select(t => t.Slug).Should().Equal(OperatorSlug, "fanus-mmt");
        list.Single(t => t.Slug == OperatorSlug).IsOperatorTenant.Should().BeTrue();
        list.Single(t => t.Slug == "fanus-mmt").KeyCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("under_score")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("a:b")]
    public async Task CreateAsync_InvalidSlug_Throws(string? slug)
    {
        await using var fx = await Fixture.CreateAsync();

        var act = () => fx.Sut.CreateAsync(new CreateAdminTenantRequest { Slug = slug, Name = "X" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_SlugOver64Characters_Throws()
    {
        await using var fx = await Fixture.CreateAsync();

        var act = () => fx.Sut.CreateAsync(
            new CreateAdminTenantRequest { Slug = new string('a', 65), Name = "X" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_MissingName_Throws()
    {
        await using var fx = await Fixture.CreateAsync();

        var act = () => fx.Sut.CreateAsync(new CreateAdminTenantRequest { Slug = "team-a", Name = "  " });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_TakenSlug_ThrowsConflict_WhateverTheCase()
    {
        await using var fx = await Fixture.CreateAsync();
        await fx.Sut.CreateAsync(new CreateAdminTenantRequest { Slug = "team-a", Name = "Team A" });

        var act = () => fx.Sut.CreateAsync(new CreateAdminTenantRequest { Slug = "TEAM-A", Name = "Again" });

        (await act.Should().ThrowAsync<TenantSlugConflictException>()).Which.Slug.Should().Be("team-a");
    }

    [Fact]
    public async Task CreateAsync_UnknownPlan_Throws_NamingTheConfiguredPlans()
    {
        await using var fx = await Fixture.CreateAsync(plans: ["pro"]);

        var act = () => fx.Sut.CreateAsync(
            new CreateAdminTenantRequest { Slug = "team-a", Name = "Team A", PlanSlug = "por" });

        (await act.Should().ThrowAsync<ArgumentException>()).WithMessage("*'por'*pro*");
    }

    [Fact]
    public async Task CreateAsync_ConfiguredPlan_IsStored()
    {
        await using var fx = await Fixture.CreateAsync(plans: ["pro"]);

        var created = await fx.Sut.CreateAsync(
            new CreateAdminTenantRequest { Slug = "team-a", Name = "Team A", PlanSlug = "pro" });

        created.PlanSlug.Should().Be("pro");
    }

    [Fact]
    public async Task UpdateAsync_KeepingAPlanThatIsNoLongerConfigured_IsAllowed()
    {
        await using var fx = await Fixture.CreateAsync(plans: ["pro"]);
        var legacyId = Guid.NewGuid();
        fx.Db.Tenants.Add(new TenantEntity
        {
            Id = legacyId,
            Slug = "legacy",
            Name = "Legacy",
            PlanSlug = "retired",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await fx.Db.SaveChangesAsync();

        var updated = await fx.Sut.UpdateAsync(
            legacyId,
            new UpdateAdminTenantRequest { Name = "Legacy team", PlanSlug = "retired" });

        updated.Name.Should().Be("Legacy team");
        updated.PlanSlug.Should().Be("retired");
    }

    [Fact]
    public async Task UpdateAsync_ReplacesEditableFields_AndKeepsTheSlug()
    {
        await using var fx = await Fixture.CreateAsync();
        var created = await fx.Sut.CreateAsync(
            new CreateAdminTenantRequest { Slug = "team-a", Name = "Team A", CostCenter = "cc-1" });

        var updated = await fx.Sut.UpdateAsync(created.Id, new UpdateAdminTenantRequest { Name = "Team Alpha" });

        updated.Slug.Should().Be("team-a");
        updated.Name.Should().Be("Team Alpha");
        updated.CostCenter.Should().BeNull("the editable fields are replaced as a set");
        updated.UpdatedAt.Should().BeOnOrAfter(created.UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_UnknownTenant_ThrowsKeyNotFound()
    {
        await using var fx = await Fixture.CreateAsync();

        var act = () => fx.Sut.UpdateAsync(Guid.NewGuid(), new UpdateAdminTenantRequest { Name = "X" });

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_DropsCachedValidation_SoTheNewCostCenterAppliesAtOnce()
    {
        await using var fx = await Fixture.CreateAsync();
        var team = await fx.Sut.CreateAsync(
            new CreateAdminTenantRequest { Slug = "team-a", Name = "Team A", CostCenter = "old" });
        var secret = await fx.SeedKeyAsync(team.Id, ApiKeyRole.Inference);

        (await fx.Validator.ValidateAsync(secret)).CostCenter.Should().Be("old");

        await fx.Sut.UpdateAsync(team.Id, new UpdateAdminTenantRequest { Name = "Team A", CostCenter = "new" });

        (await fx.Validator.ValidateAsync(secret)).CostCenter.Should().Be("new");
    }

    [Fact]
    public async Task MoveKeysAsync_MovesTheKey_AndTheNextValidationSeesTheNewTenant()
    {
        await using var fx = await Fixture.CreateAsync();
        var team = await fx.Sut.CreateAsync(new CreateAdminTenantRequest { Slug = "team-a", Name = "Team A" });
        var (keyId, secret) = await fx.SeedKeyWithIdAsync(fx.OperatorTenantId, ApiKeyRole.Inference, "worker");

        // Warm the cache under the old tenant, as a key in live use would be.
        (await fx.Validator.ValidateAsync(secret)).TenantSlug.Should().Be(OperatorSlug);

        var result = await fx.Sut.MoveKeysAsync(team.Id, [keyId]);

        result.TenantSlug.Should().Be("team-a");
        result.Skipped.Should().BeEmpty();
        var moved = result.Moved.Should().ContainSingle().Subject;
        moved.Id.Should().Be(keyId);
        moved.Label.Should().Be("worker");
        moved.FromTenantId.Should().Be(fx.OperatorTenantId);
        moved.FromTenantSlug.Should().Be(OperatorSlug);

        var validated = await fx.Validator.ValidateAsync(secret);
        validated.IsSuccess.Should().BeTrue();
        validated.TenantSlug.Should().Be("team-a");
        validated.TenantId.Should().Be(team.Id);
    }

    [Fact]
    public async Task MoveKeysAsync_RecordsAnEventUnderEachTenant()
    {
        await using var fx = await Fixture.CreateAsync();
        var team = await fx.Sut.CreateAsync(new CreateAdminTenantRequest { Slug = "team-a", Name = "Team A" });
        var (keyId, _) = await fx.SeedKeyWithIdAsync(fx.OperatorTenantId, ApiKeyRole.Inference);
        var actor = Guid.NewGuid();

        await fx.Sut.MoveKeysAsync(team.Id, [keyId], actor);

        var lifecycle = new ApiKeyLifecycleEventRepository(fx.Db);
        var left = await lifecycle.ListForKeyAsync(fx.OperatorTenantId, keyId);
        var joined = await lifecycle.ListForKeyAsync(team.Id, keyId);

        left.Should().ContainSingle(e => e.Event == ApiKeyLifecycleEvent.MovedOut && e.ActorApiKeyId == actor);
        joined.Should().ContainSingle(e => e.Event == ApiKeyLifecycleEvent.MovedIn && e.ActorApiKeyId == actor);
    }

    [Fact]
    public async Task MoveKeysAsync_SkipsWhatMayNotMove_AndStillMovesTheRest()
    {
        await using var fx = await Fixture.CreateAsync();
        var team = await fx.Sut.CreateAsync(new CreateAdminTenantRequest { Slug = "team-a", Name = "Team A" });
        var (movable, _) = await fx.SeedKeyWithIdAsync(fx.OperatorTenantId, ApiKeyRole.Inference);
        var (alreadyThere, _) = await fx.SeedKeyWithIdAsync(team.Id, ApiKeyRole.Inference);
        var (actor, _) = await fx.SeedKeyWithIdAsync(fx.OperatorTenantId, ApiKeyRole.Admin);
        var missing = Guid.NewGuid();

        var result = await fx.Sut.MoveKeysAsync(team.Id, [movable, alreadyThere, actor, missing], actor);

        result.Moved.Select(k => k.Id).Should().Equal(movable);
        result.Skipped.Should().BeEquivalentTo(
            new[]
            {
                new { Id = alreadyThere, Code = MoveApiKeySkipCodes.AlreadyInTenant },
                new { Id = actor, Code = MoveApiKeySkipCodes.SelfAction },
                new { Id = missing, Code = MoveApiKeySkipCodes.NotFound },
            },
            options => options.ExcludingMissingMembers());
    }

    [Fact]
    public async Task MoveKeysAsync_LastActiveAdminKeyOfItsTenant_StaysPut()
    {
        await using var fx = await Fixture.CreateAsync();
        var team = await fx.Sut.CreateAsync(new CreateAdminTenantRequest { Slug = "team-a", Name = "Team A" });
        var (onlyAdmin, secret) = await fx.SeedKeyWithIdAsync(fx.OperatorTenantId, ApiKeyRole.Admin);

        var result = await fx.Sut.MoveKeysAsync(team.Id, [onlyAdmin]);

        result.Moved.Should().BeEmpty();
        result.Skipped.Should().ContainSingle(k => k.Code == MoveApiKeySkipCodes.LastAdminKey);
        (await fx.Validator.ValidateAsync(secret)).TenantSlug.Should().Be(OperatorSlug);
    }

    [Fact]
    public async Task MoveKeysAsync_AdminKey_MovesWhenAnotherAdminStaysBehind()
    {
        await using var fx = await Fixture.CreateAsync();
        var team = await fx.Sut.CreateAsync(new CreateAdminTenantRequest { Slug = "team-a", Name = "Team A" });
        await fx.SeedKeyWithIdAsync(fx.OperatorTenantId, ApiKeyRole.Admin);
        var (second, _) = await fx.SeedKeyWithIdAsync(fx.OperatorTenantId, ApiKeyRole.Both);

        var result = await fx.Sut.MoveKeysAsync(team.Id, [second]);

        result.Moved.Should().ContainSingle(k => k.Id == second);
    }

    [Fact]
    public async Task MoveKeysAsync_RevokedAdminKey_IsNotProtectedByTheLastAdminGuard()
    {
        await using var fx = await Fixture.CreateAsync();
        var team = await fx.Sut.CreateAsync(new CreateAdminTenantRequest { Slug = "team-a", Name = "Team A" });
        await fx.SeedKeyWithIdAsync(fx.OperatorTenantId, ApiKeyRole.Admin);
        var (revoked, _) = await fx.SeedKeyWithIdAsync(
            fx.OperatorTenantId,
            ApiKeyRole.Admin,
            revokedAt: DateTimeOffset.UtcNow);

        var result = await fx.Sut.MoveKeysAsync(team.Id, [revoked]);

        result.Moved.Should().ContainSingle(k => k.Id == revoked);
    }

    [Fact]
    public async Task MoveKeysAsync_UnknownTargetTenant_ThrowsKeyNotFound()
    {
        await using var fx = await Fixture.CreateAsync();
        var (keyId, _) = await fx.SeedKeyWithIdAsync(fx.OperatorTenantId, ApiKeyRole.Inference);

        var act = () => fx.Sut.MoveKeysAsync(Guid.NewGuid(), [keyId]);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task MoveKeysAsync_InactiveTargetTenant_Throws_AndMovesNothing()
    {
        await using var fx = await Fixture.CreateAsync();
        var inactiveId = Guid.NewGuid();
        fx.Db.Tenants.Add(new TenantEntity
        {
            Id = inactiveId,
            Slug = "parked",
            Name = "Parked",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await fx.Db.SaveChangesAsync();
        var (keyId, secret) = await fx.SeedKeyWithIdAsync(fx.OperatorTenantId, ApiKeyRole.Inference);

        var act = () => fx.Sut.MoveKeysAsync(inactiveId, [keyId]);

        await act.Should().ThrowAsync<TenantInactiveException>();
        (await fx.Validator.ValidateAsync(secret)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task MoveKeysAsync_NoUsableIds_Throws()
    {
        await using var fx = await Fixture.CreateAsync();

        var act = () => fx.Sut.MoveKeysAsync(fx.OperatorTenantId, [Guid.Empty]);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task MoveKeysAsync_MoreThanTheBatchCeiling_Throws()
    {
        await using var fx = await Fixture.CreateAsync();
        var ids = Enumerable.Range(0, TenantAdminService.MaxKeysPerMove + 1).Select(_ => Guid.NewGuid()).ToArray();

        var act = () => fx.Sut.MoveKeysAsync(fx.OperatorTenantId, ids);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private int _keySequence;

        private Fixture(GatewayDbContext db, TenantAdminService sut, ApiKeyValidator validator, Guid operatorTenantId)
        {
            Db = db;
            Sut = sut;
            Validator = validator;
            OperatorTenantId = operatorTenantId;
        }

        public GatewayDbContext Db { get; }

        public TenantAdminService Sut { get; }

        public ApiKeyValidator Validator { get; }

        public Guid OperatorTenantId { get; }

        public static async Task<Fixture> CreateAsync(string[]? plans = null)
        {
            var options = new DbContextOptionsBuilder<GatewayDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new GatewayDbContext(options);

            var operatorTenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantEntity
            {
                Id = operatorTenantId,
                Slug = OperatorSlug,
                Name = "Default",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();

            var apiKeys = new ApiKeyRepository(db);
            var tenants = new TenantRepository(db);
            var securityOptions = Options.Create(new GatewaySecurityOptions { KeyPepper = Pepper });
            var validator = new ApiKeyValidator(
                apiKeys,
                tenants,
                new MemoryCache(new MemoryCacheOptions()),
                securityOptions,
                new ApiKeyNegativeCache());
            var sut = new TenantAdminService(
                tenants,
                apiKeys,
                new ApiKeyLifecycleEventRepository(db),
                validator,
                new OperatorTenantConfiguration(OperatorSlug),
                plans is null ? null : new FixedConfigProvider(plans));

            return new Fixture(db, sut, validator, operatorTenantId);
        }

        public async Task<string> SeedKeyAsync(Guid tenantId, ApiKeyRole role) =>
            (await SeedKeyWithIdAsync(tenantId, role)).Secret;

        public async Task<(Guid Id, string Secret)> SeedKeyWithIdAsync(
            Guid tenantId,
            ApiKeyRole role,
            string? label = null,
            DateTimeOffset? revokedAt = null)
        {
            var secret = $"sk-Autobahn-tenant-admin-test-{Interlocked.Increment(ref _keySequence):D4}";
            var id = Guid.NewGuid();
            Db.ApiKeys.Add(new ApiKeyEntity
            {
                Id = id,
                TenantId = tenantId,
                KeyHash = ApiKeyHashing.Hash(secret, Pepper),
                KeyPrefix = ApiKeyHashing.CreatePrefix(secret),
                Role = role,
                Scopes = [],
                Label = label,
                RevokedAt = revokedAt,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await Db.SaveChangesAsync();
            return (id, secret);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedConfigProvider(string[] plans) : IGatewayConfigProvider
    {
        public GatewayConfigSnapshot Current { get; } = new()
        {
            RateLimits = new RateLimitsConfigSection
            {
                Plans = plans.ToDictionary(
                    p => p,
                    _ => new RateLimitPolicy(600, 0, 0),
                    StringComparer.OrdinalIgnoreCase),
            },
        };
    }
}
