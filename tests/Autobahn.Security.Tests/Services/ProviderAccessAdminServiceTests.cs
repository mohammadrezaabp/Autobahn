using NSubstitute;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;
using Autobahn.Security.Services;

namespace Autobahn.Security.Tests.Services;

public sealed class ProviderAccessAdminServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid KeyId = Guid.NewGuid();

    private const string PaidModel = "qwen/qwen3.8-27b";

    private readonly IProviderAccessRepository _access = Substitute.For<IProviderAccessRepository>();
    private readonly IModelGrantRepository _tenantGrants = Substitute.For<IModelGrantRepository>();
    private readonly IApiKeyModelGrantRepository _keyGrants = Substitute.For<IApiKeyModelGrantRepository>();
    private readonly IModelGrantService _grantService = Substitute.For<IModelGrantService>();

    [Fact]
    public async Task OpeningAKey_UnderAClosedTenant_IsRefused()
    {
        Switches();

        var act = () => CreateSut().SetApiKeyAccessAsync(TenantId, KeyId, "openrouter", open: true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*operator*");
        await _access.DidNotReceiveWithAnyArgs().SetAsync(default, default, default!, default);
    }

    [Fact]
    public async Task OpeningAKey_UnderAnOpenTenant_WritesTheSwitchAndDropsTheCache()
    {
        Switches(Switch(null, true));

        var change = await CreateSut().SetApiKeyAccessAsync(TenantId, KeyId, "OpenRouter", open: true);

        change.Should().Be(new ProviderAccessChange(ModelProviders.OpenRouter, true, false));
        await _access.Received(1).SetAsync(TenantId, KeyId, ModelProviders.OpenRouter, true, Arg.Any<CancellationToken>());
        _grantService.Received(1).InvalidateProviderAccess(TenantId);
    }

    [Fact]
    public async Task ClosingAKey_IsAllowedWhateverTheTenantSays()
    {
        Switches();
        _access.SetAsync(TenantId, KeyId, ModelProviders.OpenRouter, false, Arg.Any<CancellationToken>()).Returns(true);

        var change = await CreateSut().SetApiKeyAccessAsync(TenantId, KeyId, "openrouter", open: false);

        change.PreviousOpen.Should().BeTrue();
    }

    [Theory]
    [InlineData("self-hosted")]
    [InlineData("together")]
    [InlineData("")]
    public async Task OnlyAPaidKnownProvider_HasASwitch(string provider)
    {
        var act = () => CreateSut().SetTenantAccessAsync(TenantId, provider, open: true);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AKeyOfAnotherTenant_IsNotReachable()
    {
        var act = () => CreateSut().GetApiKeyAccessAsync(Guid.NewGuid(), KeyId);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task GetTenantAccess_ListsEveryProvider_AndCountsOpenKeys()
    {
        Switches(Switch(null, true), Switch(KeyId, true), Switch(Guid.NewGuid(), false));

        var response = await CreateSut().GetTenantAccessAsync(TenantId);

        response.Providers.Select(p => p.Provider).Should().Equal(ModelProviders.All);
        var openRouter = response.Providers[1];
        openRouter.Paid.Should().BeTrue();
        openRouter.Open.Should().BeTrue();
        openRouter.OpenKeyCount.Should().Be(1);
        response.Providers[0].Open.Should().BeTrue("self-hosted has no switch to close");
    }

    [Fact]
    public async Task Check_NamesTheRuleThatRefuses_ThenAllowsOnceEverythingIsOpen()
    {
        _keyGrants.ListByApiKeyAsync(KeyId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ApiKeyModelGrantRecord>>(
                [new ApiKeyModelGrantRecord(Guid.NewGuid(), KeyId, "qwen/*", GrantEffect.Allow)]));
        _tenantGrants.ListByTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ModelGrantRecord>>([]));
        var sut = CreateSut();

        Switches();
        var closed = await sut.CheckAsync(TenantId, KeyId, PaidModel);
        closed.Allowed.Should().BeFalse();
        closed.Reason.Should().Be("provider_closed_for_tenant");
        closed.Provider.Should().Be(ModelProviders.OpenRouter);

        Switches(Switch(null, true), Switch(KeyId, true));
        var open = await sut.CheckAsync(TenantId, KeyId, PaidModel);
        open.Allowed.Should().BeTrue();
        open.Reason.Should().BeNull();
        open.Rule.Should().Be("qwen/*");
    }

    [Fact]
    public async Task Check_UnknownModel_SaysSo()
    {
        var result = await CreateSut().CheckAsync(TenantId, KeyId, "nope");

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("model_not_found");
    }

    private void Switches(params ProviderAccessRecord[] records) =>
        _access.ListByTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProviderAccessRecord>>(records));

    private static ProviderAccessRecord Switch(Guid? keyId, bool open) =>
        new(Guid.NewGuid(), TenantId, keyId, ModelProviders.OpenRouter, open);

    private ProviderAccessAdminService CreateSut()
    {
        var apiKeys = Substitute.For<IApiKeyRepository>();
        apiKeys.GetByIdAsync(KeyId, Arg.Any<CancellationToken>())
            .Returns(new ApiKeyRecord(
                KeyId, TenantId, "hash", "sk-prefix", ApiKeyRole.Inference, [], null, null, DateTimeOffset.UtcNow, null));

        var registry = Substitute.For<IModelRegistry>();
        registry.TryGetModel(PaidModel, out Arg.Any<ModelConfig?>())
            .Returns(call =>
            {
                call[1] = new ModelConfig { Id = PaidModel, Url = "https://openrouter.ai/api" };
                return true;
            });

        return new ProviderAccessAdminService(_access, _tenantGrants, _keyGrants, apiKeys, registry, _grantService);
    }
}
