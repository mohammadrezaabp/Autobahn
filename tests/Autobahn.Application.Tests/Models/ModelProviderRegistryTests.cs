using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;

namespace Autobahn.Application.Tests.Models;

/// <summary>
/// The paid providers the gateway tells apart, and how a backend's provider is read once a model
/// can have a standby somewhere else.
/// </summary>
public sealed class ModelProviderRegistryTests
{
    [Theory]
    [InlineData("https://openrouter.ai/api", ModelProviders.OpenRouter)]
    [InlineData("https://api.openai.com", ModelProviders.OpenAi)]
    [InlineData("https://api.deepseek.com/v1", ModelProviders.DeepSeek)]
    [InlineData("https://api.deepgram.com", ModelProviders.Deepgram)]
    [InlineData("https://api.elevenlabs.io", ModelProviders.ElevenLabs)]
    [InlineData("https://eu.api.openai.com", ModelProviders.OpenAi)]
    [InlineData("http://127.0.0.1:2220", ModelProviders.SelfHosted)]
    [InlineData("https://notopenai.com", ModelProviders.SelfHosted)]
    [InlineData("not a url", ModelProviders.SelfHosted)]
    public void FromUrl_ReadsTheProviderFromTheHost(string url, string expected)
    {
        ModelProviders.FromUrl(url).Should().Be(expected);
    }

    [Fact]
    public void EveryPaidProvider_IsRecognised_Named_AndClosedByDefault()
    {
        foreach (var definition in ModelProviders.Paid)
        {
            ModelProviders.IsPaid(definition.Id).Should().BeTrue();
            ModelProviders.All.Should().Contain(definition.Id);
            ModelProviders.DisplayName(definition.Id).Should().Be(definition.DisplayName);
            ModelGrantEvaluator.IsProviderOpen([], null, definition.Id).Should().BeFalse();
            ModelGrantPattern.ProviderOf(ModelGrantPattern.ForProvider(definition.Id)).Should().Be(definition.Id);
        }
    }

    /// <summary>The speech providers are gated and listed, and marked as not yet callable.</summary>
    [Fact]
    public void SpeechProviders_AreNotForwardable()
    {
        ModelProviders.TryGetPaid(ModelProviders.Deepgram, out var deepgram).Should().BeTrue();
        deepgram.Forwardable.Should().BeFalse();
        ModelProviders.TryGetPaid(ModelProviders.ElevenLabs, out var elevenLabs).Should().BeTrue();
        elevenLabs.Forwardable.Should().BeFalse();
        ModelProviders.TryGetPaid(ModelProviders.OpenAi, out var openAi).Should().BeTrue();
        openAi.Forwardable.Should().BeTrue();
    }

    /// <summary>
    /// The case standby exists for: the model's own hardware first, a provider only when it is
    /// down. Reading the model as paid would close it to every key not opened for the provider.
    /// </summary>
    [Fact]
    public void Resolve_ASelfHostedModelWithAPaidStandby_IsStillSelfHosted()
    {
        var model = ModelWith(
            Backend("gpu", "http://127.0.0.1:2220"),
            Backend("openrouter", "https://openrouter.ai/api", BackendPriorities.Standby));

        ModelProviders.Resolve(model).Should().Be(ModelProviders.SelfHosted);
        ModelProviders.ResolveBackend(model, model.Backends![0]).Should().Be(ModelProviders.SelfHosted);
        ModelProviders.ResolveBackend(model, model.Backends![1]).Should().Be(ModelProviders.OpenRouter);
    }

    [Fact]
    public void Resolve_APaidPrimaryAmongThePrimaries_MakesTheModelPaid()
    {
        var model = ModelWith(
            Backend("gpu", "http://127.0.0.1:2220"),
            Backend("deepseek", "https://api.deepseek.com"));

        ModelProviders.Resolve(model).Should().Be(ModelProviders.DeepSeek);
    }

    [Fact]
    public void Resolve_AModelWithOnlyStandbys_IsReadFromThem()
    {
        var model = ModelWith(Backend("only", "https://openrouter.ai/api", BackendPriorities.Standby));

        ModelProviders.Resolve(model).Should().Be(ModelProviders.OpenRouter);
    }

    [Fact]
    public void ResolveBackend_APrimary_FollowsTheOperatorsCorrection()
    {
        var model = ModelWith(Backend("mock", "http://127.0.0.1:18080"));
        model.Provider = ModelProviders.OpenRouter;

        ModelProviders.ResolveBackend(model, model.Backends![0]).Should().Be(ModelProviders.OpenRouter);
    }

    [Theory]
    [InlineData(null, BackendPriorities.Primary)]
    [InlineData("", BackendPriorities.Primary)]
    [InlineData("PRIMARY", BackendPriorities.Primary)]
    [InlineData(" Standby ", BackendPriorities.Standby)]
    public void BackendPriority_Normalizes(string? written, string expected)
    {
        BackendPriorities.TryNormalize(written, out var normalized, out var error).Should().BeTrue();
        normalized.Should().Be(expected);
        error.Should().BeNull();
    }

    [Fact]
    public void ModelConfigValidation_RefusesAnUnknownPriority()
    {
        var model = ModelWith(Backend("gpu", "http://127.0.0.1:2220", "backup"));

        ModelConfigValidation.TryValidate(model, out var error).Should().BeFalse();
        error.Should().Contain("priority 'backup'");
    }

    [Fact]
    public void Normalize_KeepsTheStandbyFields_AndTrimsTheUpstreamName()
    {
        var model = ModelWith(Backend("or", "https://openrouter.ai/api", "STANDBY", "  qwen/qwen3-27b "));

        var backend = ModelBackends.Normalize(model).Backends![0];
        var clone = ModelBackends.Clone(backend);

        clone.Priority.Should().Be(BackendPriorities.Standby);
        clone.UpstreamModel.Should().Be("qwen/qwen3-27b");
        clone.IsStandby().Should().BeTrue();
    }

    private static ModelConfig ModelWith(params ModelBackendConfig[] backends) =>
        new() { Id = "Qwen3-27B", Backends = [.. backends] };

    private static ModelBackendConfig Backend(
        string name,
        string url,
        string priority = BackendPriorities.Primary,
        string? upstreamModel = null) =>
        new() { Name = name, Url = url, Priority = priority, UpstreamModel = upstreamModel };
}
