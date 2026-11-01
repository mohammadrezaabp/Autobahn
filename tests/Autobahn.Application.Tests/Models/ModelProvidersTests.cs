using Autobahn.Domain.Models;

namespace Autobahn.Application.Tests.Models;

public sealed class ModelProvidersTests
{
    [Theory]
    [InlineData("https://openrouter.ai/api", ModelProviders.OpenRouter)]
    [InlineData("https://OpenRouter.ai/api/v1", ModelProviders.OpenRouter)]
    [InlineData("https://eu.openrouter.ai/api", ModelProviders.OpenRouter)]
    [InlineData("http://10.0.0.5:8000", ModelProviders.SelfHosted)]
    [InlineData("https://notopenrouter.ai/api", ModelProviders.SelfHosted)]
    [InlineData("https://openrouter.ai.example.com/api", ModelProviders.SelfHosted)]
    public void Resolve_ReadsTheProviderFromTheBackendAddress(string url, string expected)
    {
        ModelProviders.Resolve(new ModelConfig { Id = "m", Url = url }).Should().Be(expected);
    }

    [Fact]
    public void Resolve_OneOpenRouterBackendAmongSeveral_MakesTheModelOpenRouter()
    {
        var model = new ModelConfig
        {
            Id = "m",
            Backends =
            [
                new ModelBackendConfig { Name = "gpu", Url = "http://10.0.0.5:8000" },
                new ModelBackendConfig { Name = "standby", Url = "https://openrouter.ai/api" },
            ],
        };

        ModelProviders.Resolve(model).Should().Be(ModelProviders.OpenRouter);
    }

    [Fact]
    public void Resolve_PrefersTheProviderAnOperatorSet()
    {
        ModelProviders.Resolve(new ModelConfig { Id = "m", Url = "http://proxy.internal", Provider = "OpenRouter" })
            .Should().Be(ModelProviders.OpenRouter);
        ModelProviders.Resolve(new ModelConfig { Id = "m", Url = "https://openrouter.ai/api", Provider = "self-hosted" })
            .Should().Be(ModelProviders.SelfHosted);
    }

    [Fact]
    public void TryNormalize_RejectsAnUnknownProvider_AndAcceptsBlank()
    {
        ModelProviders.TryNormalize("together", out _, out var error).Should().BeFalse();
        error.Should().Contain("not recognised");

        ModelProviders.TryNormalize(" ", out var blank, out _).Should().BeTrue();
        blank.Should().BeNull();
    }

    [Fact]
    public void Validation_RejectsAModelWithAnUnknownProvider()
    {
        ModelConfigValidation.TryValidate(
                new ModelConfig { Id = "m", Url = "http://10.0.0.5:8000", Provider = "nope" },
                out var error)
            .Should().BeFalse();
        error.Should().Contain("provider");
    }
}
