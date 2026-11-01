using Autobahn.Application.Diagnostics;

namespace Autobahn.Application.Tests.Diagnostics;

public sealed class LogTextTests
{
    [Theory]
    [InlineData("/v1/chat\r\nFORGED successful-login")]
    [InlineData("request\nFORGED")]
    [InlineData("request\rFORGED")]
    [InlineData("request\u0085FORGED\u2028FORGED\u2029FORGED")]
    public void UntrustedFieldsCannotCreateAdditionalLogLines(string input)
    {
        var result = LogText.OneLine(input);
        result.Should().Contain("FORGED");
        result.Should().NotContainAny("\r", "\n", "\u0085", "\u2028", "\u2029");
    }

    [Fact]
    public void EndpointDiagnosticsDoNotContainCredentialsOrQuerySecrets()
    {
        var result = LogText.Endpoint(new Uri("https://test-user:test-password@backend.example/v1/chat?api_key=test-secret#private"));
        result.Should().Be("https://backend.example/v1/chat");
    }

    [Fact]
    public void OversizedFieldsAreBoundedAndNullIsSafe()
    {
        LogText.OneLine(new string('a', 100_000)).Length.Should().BeLessThanOrEqualTo(1024);
        LogText.OneLine(null).Should().BeEmpty();
    }
}
