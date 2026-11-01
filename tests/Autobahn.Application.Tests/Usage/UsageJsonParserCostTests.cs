using System.Text;
using Autobahn.Application.Usage;

namespace Autobahn.Application.Tests.Usage;

/// <summary>
/// The cost a provider reports and the cached part of the prompt, read beside the token counts.
/// </summary>
public sealed class UsageJsonParserCostTests
{
    private static ParsedUsage Parse(string json) => UsageJsonParser.Parse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Parse_ReadsTheCostOpenRouterReports()
    {
        var usage = Parse("""{"usage":{"prompt_tokens":120,"completion_tokens":30,"total_tokens":150,"cost":0.00042}}""");

        usage.Kind.Should().Be(UsageParseKind.Split);
        usage.Cost.Should().Be(0.00042m);
        usage.PromptTokens.Should().Be(120);
    }

    [Fact]
    public void Parse_WithoutACost_LeavesItNull_SoThePriceIsTheFallback()
    {
        Parse("""{"usage":{"prompt_tokens":120,"completion_tokens":30}}""").Cost.Should().BeNull();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"0.5\"")]
    [InlineData("-1")]
    [InlineData("1e12")]
    public void Parse_ACostThatIsNotAPlausibleAmount_IsNotACost(string cost)
    {
        var usage = Parse("{\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"cost\":" + cost + "}}");

        usage.Cost.Should().BeNull();
        usage.HasUsage.Should().BeTrue("the token counts are still good");
    }

    [Fact]
    public void Parse_ReadsCachedTokensInTheOpenAiShape()
    {
        var usage = Parse(
            """{"usage":{"prompt_tokens":1000,"completion_tokens":10,"prompt_tokens_details":{"cached_tokens":800}}}""");

        usage.CachedPromptTokens.Should().Be(800);
    }

    [Fact]
    public void Parse_ReadsCachedTokensInTheDeepSeekShape()
    {
        var usage = Parse(
            """{"usage":{"prompt_tokens":1000,"completion_tokens":10,"prompt_cache_hit_tokens":600,"prompt_cache_miss_tokens":400}}""");

        usage.CachedPromptTokens.Should().Be(600);
    }

    [Fact]
    public void Parse_ReadsAudioTokensOnBothSides()
    {
        var usage = Parse(
            """{"usage":{"prompt_tokens":100,"completion_tokens":50,"prompt_tokens_details":{"cached_tokens":0,"audio_tokens":30},"completion_tokens_details":{"reasoning_tokens":0,"audio_tokens":40}}}""");

        usage.AudioTokens.Should().Be(70);
        usage.PromptTokens.Should().Be(100);
        usage.CompletionTokens.Should().Be(50);
    }

    [Fact]
    public void Parse_NoAudioDetails_NoAudioTokens()
    {
        var usage = Parse("""{"usage":{"prompt_tokens":100,"completion_tokens":50}}""");

        usage.AudioTokens.Should().Be(0);
    }

    [Fact]
    public void Parse_MoreCachedTokensThanPromptTokens_IsIgnored()
    {
        var usage = Parse(
            """{"usage":{"prompt_tokens":10,"completion_tokens":1,"prompt_tokens_details":{"cached_tokens":800}}}""");

        usage.CachedPromptTokens.Should().Be(0);
    }

    /// <summary>A rerank call is priced per request: a cost and no tokens is still usage to bill.</summary>
    [Fact]
    public void Parse_ACostWithNoTokens_IsCostOnlyUsage()
    {
        var usage = Parse("""{"usage":{"cost":0.002}}""");

        usage.Kind.Should().Be(UsageParseKind.CostOnly);
        usage.HasUsage.Should().BeTrue();
        usage.Cost.Should().Be(0.002m);
        usage.BillableTokenTotal.Should().Be(0);
    }

    [Fact]
    public void ParseSseText_ReadsTheCostFromTheTerminalFrame()
    {
        const string sse = """
            data: {"choices":[{"delta":{"content":"hi"}}]}

            data: {"choices":[],"usage":{"prompt_tokens":7,"completion_tokens":2,"cost":0.0000135}}

            data: [DONE]
            """;

        var usage = UsageJsonParser.ParseSseText(sse);

        usage.Cost.Should().Be(0.0000135m);
        usage.CompletionTokens.Should().Be(2);
    }

    /// <summary>The nested details object must not unbalance the scan of an oversized body's tail.</summary>
    [Fact]
    public void ParseUsageFragment_ReadsCostAndCacheFromATruncatedBody()
    {
        var tail = Encoding.UTF8.GetBytes(
            """..."}],"usage":{"prompt_tokens":50,"completion_tokens":5,"prompt_tokens_details":{"cached_tokens":20},"cost":0.01}}""");

        var usage = UsageJsonParser.ParseUsageFragment(tail);

        usage.Cost.Should().Be(0.01m);
        usage.CachedPromptTokens.Should().Be(20);
    }
}
