using NSubstitute;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;
using Autobahn.Security.Services;

namespace Autobahn.Security.Tests.Services;

public sealed class ModelGrantAdminServiceTests
{
    private const string CanonicalModelId = "local-mock";
    private const string ModelAlias = "gpt-local";

    [Fact]
    public async Task ReplaceApiKeyGrants_WithAlias_StoresCanonicalId()
    {
        var tenantId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var registry = CreateRegistryWithAlias();
        var apiKeys = Substitute.For<IApiKeyRepository>();
        apiKeys.GetByIdAsync(apiKeyId, Arg.Any<CancellationToken>())
            .Returns(new ApiKeyRecord(
                apiKeyId,
                tenantId,
                "hash",
                "sk-prefix",
                ApiKeyRole.Inference,
                [],
                ExpiresAt: null,
                RevokedAt: null,
                DateTimeOffset.UtcNow,
                LastUsedAt: null,
                Label: null,
                Assignee: null,
                Description: null,
                CostCenter: null));

        var apiKeyGrants = Substitute.For<IApiKeyModelGrantRepository>();
        var tenantGrants = Substitute.For<IModelGrantRepository>();
        var grantService = Substitute.For<IModelGrantService>();
        var sut = new ModelGrantAdminService(
            tenantGrants,
            apiKeyGrants,
            apiKeys,
            registry,
            grantService);

        var response = await sut.ReplaceApiKeyGrantsAsync(
            tenantId,
            apiKeyId,
            new ReplaceModelGrantsRequest { ModelIds = [ModelAlias] });

        response.ModelIds.Should().ContainSingle().Which.Should().Be(CanonicalModelId);
        await apiKeyGrants.Received(1)
            .ReplaceForApiKeyAsync(
                apiKeyId,
                Arg.Is<IReadOnlyList<(string ModelPattern, GrantEffect Effect)>>(grants =>
                    grants.Count == 1 && grants[0].ModelPattern == CanonicalModelId && grants[0].Effect == GrantEffect.Allow),
                Arg.Any<CancellationToken>());
        grantService.Received(1).InvalidateApiKeyGrants(apiKeyId);
    }

    [Fact]
    public async Task ReplaceApiKeyGrants_WritesPatternsAsAllowAndDeniedModelsAsDeny()
    {
        var tenantId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var apiKeyGrants = Substitute.For<IApiKeyModelGrantRepository>();
        var sut = new ModelGrantAdminService(
            Substitute.For<IModelGrantRepository>(),
            apiKeyGrants,
            KeysHolding(apiKeyId, tenantId),
            CreateRegistryWithAlias(),
            Substitute.For<IModelGrantService>());

        var response = await sut.ReplaceApiKeyGrantsAsync(
            tenantId,
            apiKeyId,
            new ReplaceModelGrantsRequest { Patterns = [" qwen/* ", "openrouter:*"], DeniedModelIds = [ModelAlias] });

        response.Patterns.Should().Equal("qwen/*", "openrouter:*");
        response.DeniedModelIds.Should().Equal(CanonicalModelId);
        await apiKeyGrants.Received(1).ReplaceForApiKeyAsync(
            apiKeyId,
            Arg.Is<IReadOnlyList<(string ModelPattern, GrantEffect Effect)>>(grants =>
                grants.Count == 3 &&
                grants.Count(g => g.Effect == GrantEffect.Allow) == 2 &&
                grants.Single(g => g.Effect == GrantEffect.Deny).ModelPattern == CanonicalModelId),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("*")]
    [InlineData("qwen/*/x")]
    [InlineData("qwen")]
    public async Task ReplaceApiKeyGrants_RejectsAPatternThatIsNotAPrefix(string pattern)
    {
        var tenantId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var sut = new ModelGrantAdminService(
            Substitute.For<IModelGrantRepository>(),
            Substitute.For<IApiKeyModelGrantRepository>(),
            KeysHolding(apiKeyId, tenantId),
            CreateRegistryWithAlias(),
            Substitute.For<IModelGrantService>());

        var act = () => sut.ReplaceApiKeyGrantsAsync(
            tenantId,
            apiKeyId,
            new ReplaceModelGrantsRequest { Patterns = [pattern] });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReplaceApiKeyGrants_RejectsAModelBothAllowedAndDenied()
    {
        var tenantId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var sut = new ModelGrantAdminService(
            Substitute.For<IModelGrantRepository>(),
            Substitute.For<IApiKeyModelGrantRepository>(),
            KeysHolding(apiKeyId, tenantId),
            CreateRegistryWithAlias(),
            Substitute.For<IModelGrantService>());

        var act = () => sut.ReplaceApiKeyGrantsAsync(
            tenantId,
            apiKeyId,
            new ReplaceModelGrantsRequest { ModelIds = [CanonicalModelId], DeniedModelIds = [ModelAlias] });

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*both allowed and denied*");
    }

    private static IApiKeyRepository KeysHolding(Guid apiKeyId, Guid tenantId)
    {
        var apiKeys = Substitute.For<IApiKeyRepository>();
        apiKeys.GetByIdAsync(apiKeyId, Arg.Any<CancellationToken>())
            .Returns(new ApiKeyRecord(
                apiKeyId, tenantId, "hash", "sk-prefix", ApiKeyRole.Inference, [], null, null, DateTimeOffset.UtcNow, null));
        return apiKeys;
    }

    [Fact]
    public async Task ReplaceTenantGrants_WithAliasAndCanonicalId_DeduplicatesToCanonical()
    {
        var tenantId = Guid.NewGuid();
        var registry = CreateRegistryWithAlias();
        var tenantGrants = Substitute.For<IModelGrantRepository>();
        var grantService = Substitute.For<IModelGrantService>();
        var sut = new ModelGrantAdminService(
            tenantGrants,
            Substitute.For<IApiKeyModelGrantRepository>(),
            Substitute.For<IApiKeyRepository>(),
            registry,
            grantService);

        var response = await sut.ReplaceTenantGrantsAsync(
            tenantId,
            new ReplaceModelGrantsRequest { ModelIds = [ModelAlias, CanonicalModelId] });

        response.ModelIds.Should().ContainSingle().Which.Should().Be(CanonicalModelId);
        await tenantGrants.Received(1)
            .ReplaceForTenantAsync(
                tenantId,
                Arg.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == CanonicalModelId),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceApiKeyGrants_UnknownModel_ThrowsArgumentException()
    {
        var registry = Substitute.For<IModelRegistry>();
        registry.TryGetModel("missing", out Arg.Any<ModelConfig?>()).Returns(false);

        var sut = new ModelGrantAdminService(
            Substitute.For<IModelGrantRepository>(),
            Substitute.For<IApiKeyModelGrantRepository>(),
            Substitute.For<IApiKeyRepository>(),
            registry,
            Substitute.For<IModelGrantService>());

        var act = () => sut.ReplaceTenantGrantsAsync(
            Guid.NewGuid(),
            new ReplaceModelGrantsRequest { ModelIds = ["missing"] });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not registered*");
    }

    private static IModelRegistry CreateRegistryWithAlias()
    {
        var model = new ModelConfig
        {
            Id = CanonicalModelId,
            Url = "http://backend",
            Aliases = [ModelAlias],
        };

        var registry = Substitute.For<IModelRegistry>();
        registry.TryGetModel(ModelAlias, out Arg.Any<ModelConfig?>())
            .Returns(call =>
            {
                call[1] = model;
                return true;
            });
        registry.TryGetModel(CanonicalModelId, out Arg.Any<ModelConfig?>())
            .Returns(call =>
            {
                call[1] = model;
                return true;
            });
        return registry;
    }
}
