using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Application.Errors;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;
using Autobahn.Domain.RateLimiting;
using Autobahn.Policy.RateLimiting;
using Autobahn.Proxy.Errors;
using Autobahn.Proxy.Middleware;
using Autobahn.Proxy.Parsing;
using Autobahn.Proxy.Routing;

namespace Autobahn.Proxy.Tests.Middleware;

/// <summary>
/// The tokens-per-minute stage of admission: what is reserved, what a refusal gives back, and how
/// the reservation is settled once the router reports what the request used.
/// </summary>
/// <remarks>
/// The limits here are sized so that the real clock cannot change an outcome: at 1,000 tokens a
/// minute a test would have to run for several seconds to refill what the assertions depend on.
/// </remarks>
public sealed class RateLimitMiddlewareTokenLimitTests
{
    private static readonly RateLimitsConfigSection TenantTokenLimit = new()
    {
        Default = new RateLimitPolicy(1000, 0, 0) { Tpm = 1000 },
    };

    [Fact]
    public async Task InvokeAsync_WhenTheTokenBudgetIsSpent_RefusesAndSaysTheUnitIsTokens()
    {
        var middleware = Create(TenantTokenLimit, ServedWithoutUsage, out var nextCalls);
        (await Invoke(middleware, maxTokens: 600)).StatusCode.Should().Be(StatusCodes.Status200OK);

        var refused = await Invoke(middleware, maxTokens: 600);

        refused.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        refused.Headers[GatewayHeaders.RateLimitUnit].ToString().Should().Be("tokens");
        refused.Headers[GatewayHeaders.RateLimitScope].ToString().Should().Be("tenant");
        refused.Headers[GatewayHeaders.RateLimitLimit].ToString().Should().Be("1000");
        refused.Headers.RetryAfter.ToString().Should().NotBeEmpty();
        nextCalls().Should().Be(1);
    }

    /// <summary>
    /// A request refused for its tokens must not also cost a request: the tenant here has one
    /// request a minute, and still has it after the token refusal.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenTokensRefuse_HandsBackTheRequestTokens()
    {
        var store = new InMemoryDistributedRateLimitStore();
        var rateLimits = new RateLimitsConfigSection
        {
            Default = new RateLimitPolicy(1000, 0, 0),
            ApiKeys = Map(("key-1", new RateLimitPolicy(0, 0, 0) { Tpm = 1000 })),
            Models = Map(("gpt-4", new RateLimitPolicy(2, 0, 0))),
        };
        var middleware = Create(rateLimits, ServedWithoutUsage, out _, store);

        (await Invoke(middleware, maxTokens: 900, apiKeyId: "key-1")).StatusCode.Should().Be(StatusCodes.Status200OK);
        (await Invoke(middleware, maxTokens: 900, apiKeyId: "key-1")).StatusCode
            .Should().Be(StatusCodes.Status429TooManyRequests);

        // The model allows two requests a minute. One was served; the refused one gave its back.
        (await Invoke(middleware, maxTokens: 10, apiKeyId: "key-2")).StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_SettlesTheReservationAgainstTheReportedUsage()
    {
        var middleware = Create(TenantTokenLimit, context => Report(context, prompt: 20, completion: 30), out _);

        // Each reserves about 500 and uses 50. Unsettled, the second would not fit in 1,000 a
        // minute; settled, five of them leave 750.
        for (var i = 0; i < 5; i++)
        {
            (await Invoke(middleware, maxTokens: 500)).StatusCode.Should().Be(StatusCodes.Status200OK);
        }
    }

    [Fact]
    public async Task InvokeAsync_WhenTheRequestIsNeverServed_HandsBackTheWholeReservation()
    {
        var middleware = Create(TenantTokenLimit, static _ => { }, out _);

        (await Invoke(middleware, maxTokens: 900)).StatusCode.Should().Be(StatusCodes.Status200OK);
        (await Invoke(middleware, maxTokens: 900)).StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheRouterThrows_StillHandsBackTheReservation()
    {
        var failing = Create(TenantTokenLimit, static _ => throw new InvalidOperationException("boom"), out _, Shared);
        var healthy = Create(TenantTokenLimit, static _ => { }, out _, Shared);

        await FluentActions.Awaiting(() => Invoke(failing, maxTokens: 900)).Should().ThrowAsync<InvalidOperationException>();

        (await Invoke(healthy, maxTokens: 900)).StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    /// <summary>
    /// An overrun is charged: the caller declared 10 tokens of output and the backend spent 2,000.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenTheResponseUsedMoreThanReserved_TheNextRequestIsRefused()
    {
        var middleware = Create(TenantTokenLimit, context => Report(context, prompt: 1500, completion: 500), out _);
        (await Invoke(middleware, maxTokens: 10)).StatusCode.Should().Be(StatusCodes.Status200OK);

        (await Invoke(middleware, maxTokens: 10)).StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task InvokeAsync_WithNoMaxTokens_ReservesTheConfiguredDefault()
    {
        var middleware = Create(
            TenantTokenLimit,
            ServedWithoutUsage,
            out _,
            options: new RateLimitingOptions { TokenReservationDefaultMaxTokens = 600 });

        (await Invoke(middleware, maxTokens: null)).StatusCode.Should().Be(StatusCodes.Status200OK);
        (await Invoke(middleware, maxTokens: null)).StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// A prompt and its completion cannot exceed the context length, so neither can the reservation
    /// — whatever <c>max_tokens</c> the client sent.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_HoldsTheReservationToTheModelContextLength()
    {
        var middleware = Create(TenantTokenLimit, ServedWithoutUsage, out _, maxContextLength: 300);

        for (var i = 0; i < 3; i++)
        {
            (await Invoke(middleware, maxTokens: 1_000_000)).StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        (await Invoke(middleware, maxTokens: 1_000_000)).StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// The feature costs nothing where it is not used: with no token limit and no per-model rule the
    /// body is not read here at all.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WithNoTokenLimitConfigured_DoesNotParseTheBody()
    {
        var middleware = Create(
            new RateLimitsConfigSection { Default = new RateLimitPolicy(1000, 0, 0) },
            static _ => { },
            out _);
        var context = CreateContext(maxTokens: 900, apiKeyId: null);

        await middleware.InvokeAsync(context);

        InferenceRequestParseCache.TryGet(context, out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ForAnUnknownModel_ReservesNothing()
    {
        var middleware = Create(TenantTokenLimit, ServedWithoutUsage, out var nextCalls);

        (await Invoke(middleware, maxTokens: 900, model: "nope")).StatusCode.Should().Be(StatusCodes.Status200OK);
        (await Invoke(middleware, maxTokens: 900, model: "nope")).StatusCode.Should().Be(StatusCodes.Status200OK);
        nextCalls().Should().Be(2);
    }

    private static readonly InMemoryDistributedRateLimitStore Shared = new();

    private static void ServedWithoutUsage(HttpContext context) =>
        RateLimitTokenUsage.Report(context, usage: null, served: true);

    private static void Report(HttpContext context, long prompt, long completion) =>
        RateLimitTokenUsage.Report(
            context,
            new UsageEvent
            {
                RequestId = "req-1",
                ModelId = "gpt-4",
                PromptTokens = prompt,
                CompletionTokens = completion,
            },
            served: true);

    private static RateLimitMiddleware Create(
        RateLimitsConfigSection rateLimits,
        Action<HttpContext> router,
        out Func<int> nextCalls,
        IDistributedRateLimitStore? store = null,
        RateLimitingOptions? options = null,
        int maxContextLength = 0)
    {
        var calls = 0;
        nextCalls = () => calls;

        var registry = Substitute.For<IModelRegistry>();
        registry.TryGetModel(Arg.Any<string>(), out Arg.Any<ModelConfig?>())
            .Returns(call =>
            {
                if (!string.Equals((string)call[0], "gpt-4", StringComparison.OrdinalIgnoreCase))
                {
                    call[1] = null;
                    return false;
                }

                call[1] = new ModelConfig
                {
                    Id = "gpt-4",
                    Url = "http://backend:8000",
                    MaxContextLength = maxContextLength,
                };
                return true;
            });

        return new RateLimitMiddleware(
            context =>
            {
                calls++;
                router(context);
                return Task.CompletedTask;
            },
            new RateLimitPlanResolver(new StubConfigProvider(new GatewayConfigSnapshot { RateLimits = rateLimits })),
            store ?? new InMemoryDistributedRateLimitStore(),
            new OpenAiErrorResponseWriter(),
            Substitute.For<IGatewayMetricsCollector>(),
            registry,
            timeProvider: TimeProvider.System,
            options: options is null ? null : Options.Create(options));
    }

    private static async Task<HttpResponse> Invoke(
        RateLimitMiddleware middleware,
        long? maxTokens,
        string? apiKeyId = null,
        string model = "gpt-4")
    {
        var context = CreateContext(maxTokens, apiKeyId, model);
        await middleware.InvokeAsync(context);
        return context.Response;
    }

    private static DefaultHttpContext CreateContext(long? maxTokens, string? apiKeyId, string model = "gpt-4")
    {
        var body = maxTokens is { } max
            ? $$"""{"model":"{{model}}","max_tokens":{{max}}}"""
            : $$"""{"model":"{{model}}"}""";

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/chat/completions";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();

        if (apiKeyId is not null)
        {
            context.Items[TenantContextKeys.HttpContextItemKey] = new TenantContext
            {
                TenantId = "tenant-acme",
                ApiKeyId = apiKeyId,
            };
        }

        return context;
    }

    private static IReadOnlyDictionary<string, RateLimitPolicy> Map(
        params (string Key, RateLimitPolicy Policy)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Policy, StringComparer.OrdinalIgnoreCase);

    private sealed class StubConfigProvider(GatewayConfigSnapshot snapshot) : IGatewayConfigProvider
    {
        public GatewayConfigSnapshot Current { get; } = snapshot;
    }
}
