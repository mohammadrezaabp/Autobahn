using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Autobahn.Integration.Tests.Support;

namespace Autobahn.Integration.Tests.Phase4;

/// <summary>
/// The tokens-per-minute limit through the whole pipeline: the limiter reserves, the router reports
/// what the upstream's usage block said, and the limiter settles. The mock upstream answers every
/// request with 18 tokens of usage.
/// </summary>
public sealed class TokenRateLimitIntegrationTests
{
    /// <summary>
    /// Each request reserves over 600 of a 1,000-token minute and uses 18. They only all fit if the
    /// unused part of each reservation comes back when its response ends.
    /// </summary>
    [Fact]
    public async Task ChatCompletions_SettleTheirReservationFromTheUpstreamUsage()
    {
        var handler = new MockUpstreamHandler();
        await using var factory = CreateFactory(handler, tpm: 1000);
        var client = factory.CreateClient();

        for (var i = 0; i < 4; i++)
        {
            using var response = await PostAsync(client, maxTokens: 600);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        handler.SendCount.Should().Be(4);
    }

    /// <summary>
    /// A 30-token minute: the first request is admitted on the full bucket and leaves 12 after its 18
    /// are settled, so the second is refused before it reaches the upstream.
    /// </summary>
    [Fact]
    public async Task ChatCompletions_WhenTheTokenBudgetIsSpent_Returns429AndNamesTheUnit()
    {
        var handler = new MockUpstreamHandler();
        await using var factory = CreateFactory(handler, tpm: 30);
        var client = factory.CreateClient();

        using var first = await PostAsync(client, maxTokens: 600);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = await PostAsync(client, maxTokens: 600);

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        second.Headers.Contains("Retry-After").Should().BeTrue();
        second.Headers.GetValues("X-Autobahn-RateLimit-Unit").Should().Equal("tokens");
        second.Headers.GetValues("X-Autobahn-RateLimit-Scope").Should().Equal("tenant");
        second.Headers.GetValues("RateLimit-Limit").Should().Equal("30");
        var payload = await second.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("error").GetProperty("code").GetString().Should().Be("rate_limit_exceeded");
        payload.GetProperty("error").GetProperty("message").GetString().Should().Contain("Tokens-per-minute");
        handler.SendCount.Should().Be(1);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory(
        MockUpstreamHandler handler,
        int tpm) =>
        GatewayWebApplicationFactory.Create(
            handler,
            configureSettings: settings =>
            {
                settings["RateLimiting:Default:Rpm"] = "1000";
                settings["RateLimiting:Default:Burst"] = "0";
                settings["RateLimiting:Default:Tpm"] = tpm.ToString();
            });

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, int maxTokens)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = "local-mock",
            max_tokens = maxTokens,
            messages = new[] { new { role = "user", content = "hi" } },
        });

        return client.PostAsync("/v1/chat/completions", new StringContent(body, Encoding.UTF8, "application/json"));
    }
}
