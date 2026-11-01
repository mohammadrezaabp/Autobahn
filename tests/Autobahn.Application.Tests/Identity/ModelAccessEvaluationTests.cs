using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;

namespace Autobahn.Application.Tests.Identity;

public sealed class ModelAccessEvaluationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid KeyId = Guid.NewGuid();

    private const string PaidModel = "qwen/qwen3.8-27b";

    [Fact]
    public void PaidModel_WithNoSwitches_IsClosedForTheTenant_EvenWhenTheKeyListsIt()
    {
        var decision = Evaluate(PaidModel, ModelProviders.OpenRouter, keyGrants: [Allow(PaidModel)]);

        decision.Refusal.Should().Be(ModelAccessRefusal.ProviderClosedForTenant);
    }

    [Fact]
    public void PaidModel_TenantOpenKeyClosed_IsClosedForTheKey()
    {
        var decision = Evaluate(
            PaidModel,
            ModelProviders.OpenRouter,
            keyGrants: [Allow(PaidModel)],
            access: [Switch(null, open: true)]);

        decision.Refusal.Should().Be(ModelAccessRefusal.ProviderClosedForKey);
    }

    [Fact]
    public void PaidModel_KeyOpenTenantClosed_IsStillClosedForTheTenant()
    {
        var decision = Evaluate(
            PaidModel,
            ModelProviders.OpenRouter,
            keyGrants: [Allow(PaidModel)],
            access: [Switch(null, open: false), Switch(KeyId, open: true)]);

        decision.Refusal.Should().Be(ModelAccessRefusal.ProviderClosedForTenant);
    }

    [Fact]
    public void PaidModel_BothOpen_StillNeedsItsPlaceOnTheKeyList()
    {
        var open = new[] { Switch(null, open: true), Switch(KeyId, open: true) };

        Evaluate(PaidModel, ModelProviders.OpenRouter, keyGrants: [], access: open)
            .Refusal.Should().Be(ModelAccessRefusal.ModelNotOnKeyList);
        Evaluate(PaidModel, ModelProviders.OpenRouter, keyGrants: [Allow(PaidModel)], access: open)
            .IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void AnotherKeysSwitch_DoesNotOpenThisKey()
    {
        var decision = Evaluate(
            PaidModel,
            ModelProviders.OpenRouter,
            keyGrants: [Allow(PaidModel)],
            access: [Switch(null, open: true), Switch(Guid.NewGuid(), open: true)]);

        decision.Refusal.Should().Be(ModelAccessRefusal.ProviderClosedForKey);
    }

    [Fact]
    public void SelfHostedModel_NeedsNoSwitch()
    {
        Evaluate("gemma", ModelProviders.SelfHosted, keyGrants: [Allow("gemma")])
            .IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void ModelMissingFromANonEmptyTenantList_IsRefusedByThatList()
    {
        var decision = ModelGrantEvaluator.Evaluate(
            [new ModelGrantRecord(Guid.NewGuid(), TenantId, "other", GrantEffect.Allow)],
            [Allow("gemma")],
            [],
            KeyId,
            "gemma",
            ModelProviders.SelfHosted);

        decision.Refusal.Should().Be(ModelAccessRefusal.ModelNotOnTenantList);
    }

    [Theory]
    [InlineData("qwen/*", "qwen/qwen3.8-27b", true)]
    [InlineData("QWEN/*", "qwen/qwen3.8-27b", true)]
    [InlineData("qwen/*", "deepseek/v4", false)]
    [InlineData("qwen/*", "qwen", false)]
    [InlineData("*", "qwen/qwen3.8-27b", false)]
    public void PrefixEntry_MatchesByTheStartOfTheId(string pattern, string modelId, bool expected)
    {
        Evaluate(modelId, ModelProviders.SelfHosted, keyGrants: [Allow(pattern)])
            .IsAllowed.Should().Be(expected);
    }

    [Fact]
    public void ProviderEntry_CoversEveryModelOfThatProviderAndNoOther()
    {
        var open = new[] { Switch(null, open: true), Switch(KeyId, open: true) };
        var grants = new[] { Allow("openrouter:*") };

        Evaluate("anything/new", ModelProviders.OpenRouter, grants, open).IsAllowed.Should().BeTrue();
        Evaluate("gemma", ModelProviders.SelfHosted, grants, open)
            .Refusal.Should().Be(ModelAccessRefusal.ModelNotOnKeyList);
    }

    [Fact]
    public void DenyEntry_WinsOverThePrefixThatAllows()
    {
        var decision = Evaluate(
            "qwen/big",
            ModelProviders.SelfHosted,
            keyGrants: [Allow("qwen/*"), new ApiKeyModelGrantRecord(Guid.NewGuid(), KeyId, "qwen/big", GrantEffect.Deny)]);

        decision.Refusal.Should().Be(ModelAccessRefusal.ModelNotOnKeyList);
        decision.Rule.Should().Be("qwen/big");
    }

    [Fact]
    public void AllowedDecision_NamesTheEntryThatAllowed()
    {
        Evaluate("qwen/small", ModelProviders.SelfHosted, keyGrants: [Allow("qwen/*")])
            .Rule.Should().Be("qwen/*");
    }

    [Theory]
    [InlineData("qwen/*", true)]
    [InlineData("openrouter:*", true)]
    [InlineData("*", false)]
    [InlineData("qwen/*/x", false)]
    [InlineData("qw*en/*", false)]
    [InlineData("qwen/", false)]
    [InlineData("", false)]
    public void TryValidateWildcard_AcceptsOnlyATrailingStarWithAPrefix(string pattern, bool expected)
    {
        ModelGrantPattern.TryValidateWildcard(pattern, out _).Should().Be(expected);
    }

    [Theory]
    [InlineData(ModelAccessRefusal.ProviderClosedForTenant, "provider_closed_for_tenant")]
    [InlineData(ModelAccessRefusal.ProviderClosedForKey, "provider_closed_for_key")]
    [InlineData(ModelAccessRefusal.ModelNotOnTenantList, "model_not_on_tenant_list")]
    [InlineData(ModelAccessRefusal.ModelNotOnKeyList, "model_not_on_key_list")]
    public void ToReason_IsTheSpellingCallersSee(ModelAccessRefusal refusal, string expected)
    {
        refusal.ToReason().Should().Be(expected);
    }

    private static ModelAccessDecision Evaluate(
        string modelId,
        string provider,
        IReadOnlyList<ApiKeyModelGrantRecord> keyGrants,
        IReadOnlyList<ProviderAccessRecord>? access = null) =>
        ModelGrantEvaluator.Evaluate([], keyGrants, access ?? [], KeyId, modelId, provider);

    private static ApiKeyModelGrantRecord Allow(string pattern) =>
        new(Guid.NewGuid(), KeyId, pattern, GrantEffect.Allow);

    private static ProviderAccessRecord Switch(Guid? keyId, bool open) =>
        new(Guid.NewGuid(), TenantId, keyId, ModelProviders.OpenRouter, open);
}
