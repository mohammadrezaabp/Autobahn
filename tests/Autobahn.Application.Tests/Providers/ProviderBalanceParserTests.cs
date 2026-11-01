using System.Text.Json;
using Autobahn.Application.Providers;

namespace Autobahn.Application.Tests.Providers;

public sealed class ProviderBalanceParserTests
{
    [Fact]
    public void OpenRouterKey_WithALimit_ReportsWhatIsLeftOfIt()
    {
        using var doc = JsonDocument.Parse(
            """{"data":{"label":"sk-or-v1-abc","usage":12.5,"limit":50,"limit_remaining":37.5,"is_free_tier":false}}""");

        ProviderBalanceParser.TryParseOpenRouterKey(doc.RootElement, out var reading, out var hasLimit).Should().BeTrue();

        hasLimit.Should().BeTrue();
        reading.Remaining.Should().Be(37.5m);
        reading.Limit.Should().Be(50m);
        reading.Used.Should().Be(12.5m);
        reading.Currency.Should().Be("USD");
    }

    /// <summary>A key with no limit of its own spends the account's credits, read from the other endpoint.</summary>
    [Fact]
    public void OpenRouterKey_WithoutALimit_SaysSo()
    {
        using var doc = JsonDocument.Parse("""{"data":{"usage":3.2,"limit":null,"limit_remaining":null}}""");

        ProviderBalanceParser.TryParseOpenRouterKey(doc.RootElement, out var reading, out var hasLimit).Should().BeTrue();

        hasLimit.Should().BeFalse();
        reading.Remaining.Should().BeNull();
        reading.Used.Should().Be(3.2m);
    }

    [Fact]
    public void OpenRouterCredits_ReportsBoughtMinusUsed()
    {
        using var doc = JsonDocument.Parse("""{"data":{"total_credits":100,"total_usage":42.25}}""");

        ProviderBalanceParser.TryParseOpenRouterCredits(doc.RootElement, out var reading).Should().BeTrue();

        reading.Remaining.Should().Be(57.75m);
    }

    [Fact]
    public void DeepSeek_PrefersTheUsdEntry_AndReadsAmountsWrittenAsStrings()
    {
        using var doc = JsonDocument.Parse(
            """{"is_available":true,"balance_infos":[{"currency":"CNY","total_balance":"110.00"},{"currency":"USD","total_balance":"15.30"}]}""");

        ProviderBalanceParser.TryParseDeepSeek(doc.RootElement, out var reading).Should().BeTrue();

        reading.Remaining.Should().Be(15.30m);
        reading.Currency.Should().Be("USD");
    }

    [Fact]
    public void DeepSeek_WithNoUsdEntry_ReportsTheCurrencyItHas()
    {
        using var doc = JsonDocument.Parse("""{"balance_infos":[{"currency":"CNY","total_balance":"110.00"}]}""");

        ProviderBalanceParser.TryParseDeepSeek(doc.RootElement, out var reading).Should().BeTrue();

        reading.Currency.Should().Be("CNY");
    }

    [Theory]
    [InlineData("""{"error":{"message":"No auth credentials found"}}""")]
    [InlineData("""[]""")]
    [InlineData("""{"data":"nope"}""")]
    public void AnAnswerThatIsNotABalance_IsNotParsed(string json)
    {
        using var doc = JsonDocument.Parse(json);

        ProviderBalanceParser.TryParseOpenRouterKey(doc.RootElement, out _, out _).Should().BeFalse();
        ProviderBalanceParser.TryParseOpenRouterCredits(doc.RootElement, out _).Should().BeFalse();
        ProviderBalanceParser.TryParseDeepSeek(doc.RootElement, out _).Should().BeFalse();
    }

    [Fact]
    public void StatusStore_AFailedRead_KeepsTheLastFiguresAndSaysWhy()
    {
        var store = new ProviderStatusStore();
        var readAt = DateTimeOffset.UtcNow;
        store.SetWatched("openrouter", true);
        store.SetBalance("openrouter", new ProviderBalanceReading(20m, 50m, 30m, "USD", readAt, null));

        store.SetBalanceError("openrouter", "The provider answered HTTP 503.");

        var reading = store.GetBalance("openrouter")!;
        reading.Remaining.Should().Be(20m);
        reading.ReadAt.Should().Be(readAt);
        reading.Error.Should().Be("The provider answered HTTP 503.");
    }

    [Fact]
    public void StatusStore_AProviderThatLosesItsKey_IsNoLongerWatched()
    {
        var store = new ProviderStatusStore();
        store.SetWatched("openrouter", true);
        store.SetBalance("openrouter", new ProviderBalanceReading(20m, null, null, "USD", DateTimeOffset.UtcNow, null));

        store.SetWatched("openrouter", false);

        store.Watched.Should().BeEmpty();
        store.GetBalance("openrouter").Should().BeNull();
    }

    [Fact]
    public void OpenAiCosts_SumsEveryBucketsResults()
    {
        using var doc = JsonDocument.Parse(
            """{"object":"page","data":[{"object":"bucket","start_time":1,"end_time":2,"results":[{"object":"organization.costs.result","amount":{"value":1.25,"currency":"usd"}},{"amount":{"value":"0.75","currency":"usd"}}]},{"results":[]}],"has_more":true,"next_page":"page_2"}""");

        ProviderBalanceParser.TryParseOpenAiCostsPage(doc.RootElement, out var used, out var next).Should().BeTrue();

        used.Should().Be(2m);
        next.Should().Be("page_2");
    }

    [Fact]
    public void OpenAiCosts_LastPage_HasNoCursor()
    {
        using var doc = JsonDocument.Parse("""{"data":[],"has_more":false,"next_page":null}""");

        ProviderBalanceParser.TryParseOpenAiCostsPage(doc.RootElement, out var used, out var next).Should().BeTrue();

        used.Should().Be(0m);
        next.Should().BeNull();
    }

    [Fact]
    public void Deepgram_SumsTheUsdBalancesOfEveryProject()
    {
        using var projects = JsonDocument.Parse("""{"projects":[{"project_id":"p1","name":"a"},{"project_id":"p2","name":"b"}]}""");
        using var first = JsonDocument.Parse("""{"balances":[{"balance_id":"b1","amount":120.5,"units":"usd"},{"balance_id":"b2","amount":3,"units":"hour"}]}""");
        using var second = JsonDocument.Parse("""{"balances":[{"balance_id":"b3","amount":9.5,"units":"usd"}]}""");

        ProviderBalanceParser.DeepgramProjectIds(projects.RootElement).Should().Equal("p1", "p2");
        ProviderBalanceParser.TryParseDeepgramBalances([first.RootElement, second.RootElement], out var reading).Should().BeTrue();

        reading.Remaining.Should().Be(130m);
        reading.Currency.Should().Be("USD");
    }

    [Fact]
    public void Deepgram_NoUsdBalance_IsNoReading()
    {
        using var doc = JsonDocument.Parse("""{"balances":[{"amount":3,"units":"hour"}]}""");

        ProviderBalanceParser.TryParseDeepgramBalances([doc.RootElement], out _).Should().BeFalse();
    }

    /// <summary>ElevenLabs counts credits, not money; reconciliation, which compares USD, leaves them out.</summary>
    [Fact]
    public void ElevenLabs_ReportsCreditsLeftOfThePeriod()
    {
        using var doc = JsonDocument.Parse(
            """{"tier":"creator","character_count":40000,"character_limit":100000,"next_character_count_reset_unix":1767225600}""");

        ProviderBalanceParser.TryParseElevenLabsSubscription(doc.RootElement, out var reading).Should().BeTrue();

        reading.Remaining.Should().Be(60000m);
        reading.Limit.Should().Be(100000m);
        reading.Used.Should().Be(40000m);
        reading.Currency.Should().Be(ProviderBalanceParser.ElevenLabsCreditUnit);
    }
}
