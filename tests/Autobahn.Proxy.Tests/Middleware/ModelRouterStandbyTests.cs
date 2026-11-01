using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Application.Errors;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;
using Autobahn.Application.Observability;
using Autobahn.Domain.RateLimiting;
using Autobahn.Application.Security;
using Autobahn.Proxy.Errors;
using Autobahn.Proxy.Forwarding;
using Autobahn.Proxy.Middleware;
using Autobahn.Proxy.Resilience;
using Yarp.ReverseProxy.Forwarder;

namespace Autobahn.Proxy.Tests.Middleware;

/// <summary>
/// A self-hosted model with a paid standby: the standby takes a request only when the model's own
/// backend cannot, only for a key opened for the standby's provider, and under that key's cap.
/// </summary>
public sealed class ModelRouterStandbyTests
{
    private const string PrimaryUrl = "http://gpu:8000";
    private const string StandbyUrl = "https://openrouter.ai/api";
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid KeyId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task WhileThePrimaryServes_TheStandbyIsNotUsed_AndNothingIsCapped()
    {
        var harness = new Harness(primaryHealthy: true, keyOpenedForProvider: true);

        var context = await harness.RunAsync();

        harness.ForwardedTo.Should().Be(PrimaryUrl);
        context.Response.Headers[GatewayHeaders.Provider].ToString().Should().Be(ModelProviders.SelfHosted);
        context.Response.Headers.ContainsKey(GatewayHeaders.Standby).Should().BeFalse();
        await harness.SpendCaps.DidNotReceiveWithAnyArgs().TryReserveAsync(default, default, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task WhenThePrimaryIsDown_AnOpenedKeyIsCarriedByTheStandby_UnderItsCap()
    {
        var harness = new Harness(primaryHealthy: false, keyOpenedForProvider: true);

        var context = await harness.RunAsync();

        harness.ForwardedTo.Should().Be(StandbyUrl);
        context.Response.Headers[GatewayHeaders.Provider].ToString().Should().Be(ModelProviders.OpenRouter);
        context.Response.Headers[GatewayHeaders.Standby].ToString().Should().Be("true");
        await harness.SpendCaps.Received(1).TryReserveAsync(
            TenantId, KeyId, ModelProviders.OpenRouter, Arg.Any<string>(), "Qwen3-27B",
            Arg.Any<long?>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        harness.Metrics.Received(1).RecordStandbyServed("Qwen3-27B", ModelProviders.OpenRouter);
    }

    /// <summary>
    /// Only a paid provider's request outlives its client: it is billed anyway, and finishing it is
    /// how its usage is recorded. A self-hosted backend still stops with the client.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OnlyAPaidProvidersRequest_IsCompletedAfterTheClientLeaves(bool primaryHealthy, bool completed)
    {
        var harness = new Harness(primaryHealthy, keyOpenedForProvider: true);

        await harness.RunAsync();

        harness.CompletedOnClientCancel.Should().Be(completed);
    }

    /// <summary>The standby is sent the name it knows the model by, not the gateway's.</summary>
    [Fact]
    public async Task TheStandbyIsSentItsOwnNameForTheModel()
    {
        var harness = new Harness(primaryHealthy: false, keyOpenedForProvider: true);

        await harness.RunAsync();

        harness.ForwardedBody.Should().Contain("\"model\":\"qwen/qwen3-27b\"");
        harness.ForwardedBody.Should().NotContain("Qwen3-27B");
    }

    /// <summary>
    /// Being on the model's list was never a decision to spend at a provider: a key that is not
    /// opened gets the answer it got before standbys existed.
    /// </summary>
    [Fact]
    public async Task WhenThePrimaryIsDown_AKeyNotOpenedForTheProvider_IsRefusedAsBefore()
    {
        var harness = new Harness(primaryHealthy: false, keyOpenedForProvider: false);

        var context = await harness.RunAsync();

        context.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
        context.Response.Headers[GatewayHeaders.ErrorCode].ToString().Should().Be("backend_unhealthy");
        harness.ForwardedTo.Should().BeNull();
    }

    [Fact]
    public async Task AKeyAtItsCap_IsRefusedWithSpendCapReached_AndNothingIsForwarded()
    {
        var harness = new Harness(primaryHealthy: false, keyOpenedForProvider: true);
        harness.SpendCaps.TryReserveAsync(
                TenantId, KeyId, ModelProviders.OpenRouter, Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<long?>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderSpendDecision(false, ProviderSpendScope.ApiKey, 5m, 5m));

        var context = await harness.RunAsync();

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.Headers[GatewayHeaders.ErrorCode].ToString().Should().Be("spend_cap_reached");
        harness.ForwardedTo.Should().BeNull();
        // The budget reservation taken a line earlier has to be handed back.
        harness.Budget.Received(1).ReleaseReservation(Arg.Any<string>());
        harness.Metrics.Received(1).RecordSpendCapRejection(ModelProviders.OpenRouter, "api_key", Arg.Any<string?>(), "Qwen3-27B");
    }

    /// <summary>A failed request produced no usage, so nothing downstream will release the cap reservation.</summary>
    [Fact]
    public async Task ARequestThatProducesNoUsage_ReleasesItsCapReservation()
    {
        var harness = new Harness(primaryHealthy: false, keyOpenedForProvider: true, forwardResult: ForwarderError.Request);

        await harness.RunAsync();

        harness.SpendCaps.Received(1).Release(Arg.Any<string>());
    }

    [Theory]
    [InlineData(StatusCodes.Status402PaymentRequired, true)]
    [InlineData(StatusCodes.Status401Unauthorized, true)]
    [InlineData(StatusCodes.Status429TooManyRequests, false)]
    [InlineData(StatusCodes.Status200OK, false)]
    public async Task AnAccountRefusalFromThePaidProvider_IsCounted(int upstreamStatus, bool counted)
    {
        var harness = new Harness(primaryHealthy: false, keyOpenedForProvider: true, upstreamStatus: upstreamStatus);

        await harness.RunAsync();

        if (counted)
        {
            harness.Metrics.Received(1).RecordProviderAccountRefusal(ModelProviders.OpenRouter, upstreamStatus);
        }
        else
        {
            harness.Metrics.DidNotReceiveWithAnyArgs().RecordProviderAccountRefusal(default!, default);
        }
    }

    private sealed class Harness
    {
        private readonly bool _primaryHealthy;
        private readonly ForwarderError _forwardResult;
        private readonly int _upstreamStatus;
        private readonly IModelGrantService _grants = Substitute.For<IModelGrantService>();

        public Harness(
            bool primaryHealthy,
            bool keyOpenedForProvider,
            ForwarderError forwardResult = ForwarderError.None,
            int upstreamStatus = StatusCodes.Status200OK)
        {
            _primaryHealthy = primaryHealthy;
            _forwardResult = forwardResult;
            _upstreamStatus = upstreamStatus;

            _grants.EvaluateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ModelAccessDecision.Allowed());
            _grants.IsProviderOpenAsync(TenantId, KeyId, ModelProviders.OpenRouter, Arg.Any<CancellationToken>())
                .Returns(keyOpenedForProvider);

            Budget.TryReserveAsync(
                    Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(BudgetCheckResult.Allowed);
            SpendCaps.TryReserveAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<long?>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(ProviderSpendDecision.Allowed);
        }

        public IBudgetEnforcementService Budget { get; } = Substitute.For<IBudgetEnforcementService>();

        public IProviderSpendCapService SpendCaps { get; } = Substitute.For<IProviderSpendCapService>();

        public IGatewayMetricsCollector Metrics { get; } = Substitute.For<IGatewayMetricsCollector>();

        public string? ForwardedTo { get; private set; }

        public bool? CompletedOnClientCancel { get; private set; }

        public string? ForwardedBody { get; private set; }

        public async Task<DefaultHttpContext> RunAsync()
        {
            var configPath = Path.Combine(Path.GetTempPath(), "Autobahn-standby-" + Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "models": [
                    {
                      "id": "Qwen3-27B",
                      "backends": [
                        { "name": "gpu", "url": "http://gpu:8000" },
                        {
                          "name": "openrouter",
                          "url": "https://openrouter.ai/api",
                          "priority": "standby",
                          "upstreamModel": "qwen/qwen3-27b",
                          "upstreamAuth": { "type": "bearer", "envVar": "OPENROUTER_API_KEY" }
                        }
                      ]
                    }
                  ]
                }
                """);

            try
            {
                var registry = new Autobahn.Registry.Services.ModelRegistryService(
                    NullLogger<Autobahn.Registry.Services.ModelRegistryService>.Instance);
                await registry.LoadModelsAsync(configPath);

                var context = CreateContext();
                await CreateMiddleware(registry).InvokeAsync(context);
                return context;
            }
            finally
            {
                File.Delete(configPath);
            }
        }

        private static DefaultHttpContext CreateContext()
        {
            var bodyBytes = Encoding.UTF8.GetBytes("""{"model":"Qwen3-27B","stream":false,"messages":[]}""");
            var context = new DefaultHttpContext
            {
                Request =
                {
                    Method = HttpMethods.Post,
                    Path = "/v1/chat/completions",
                    Body = new MemoryStream(bodyBytes),
                    ContentType = "application/json",
                    ContentLength = bodyBytes.Length,
                },
                Response = { Body = new MemoryStream() },
            };

            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(GatewayAuthClaims.TenantId, TenantId.ToString()),
                new Claim(GatewayAuthClaims.ApiKeyId, KeyId.ToString()),
                new Claim(GatewayAuthClaims.Role, ApiKeyRole.Inference.ToString()),
            ],
            GatewayAuthSchemes.ApiKey));
            context.Items[TenantContextKeys.HttpContextItemKey] = new TenantContext
            {
                TenantId = TenantId.ToString(),
                ApiKeyId = KeyId.ToString(),
                Role = ApiKeyRole.Inference,
            };
            return context;
        }

        private ModelRouterMiddleware CreateMiddleware(IModelRegistry registry)
        {
            var health = Substitute.For<IBackendHealthStore>();
            health.IsBackendHealthy("Qwen3-27B", "gpu").Returns(_primaryHealthy);
            health.IsBackendHealthy("Qwen3-27B", "openrouter").Returns(true);

            var authState = Substitute.For<IGatewayAuthenticationState>();
            authState.IsAuthenticationRequired.Returns(true);

            var requestTracker = Substitute.For<IRequestTracker>();
            requestTracker.BeginInferenceRequest(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<MetricCaller>())
                .Returns(_ => Substitute.For<IInferenceRequestScope>());

            var options = Options.Create(new GatewayOptions());
            var rateLimitResolver = Substitute.For<IRateLimitPolicyResolver>();
            rateLimitResolver.Resolve(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
                .Returns(new RateLimitPolicy(10_000, 1_000, 1_000));

            var tokens = Substitute.For<IUpstreamBearerTokenResolver>();
            tokens.ResolveBearerToken(Arg.Is<UpstreamAuthConfig?>(a => a != null)).Returns("company-key");

            var forwarder = Substitute.For<IInferenceHttpForwarder>();
            forwarder.SendAsync(
                    Arg.Any<HttpContext>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<StreamingHttpTransformer>(),
                    Arg.Any<bool>(),
                    Arg.Any<InferenceForwardTimeouts>(),
                    Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    var context = call.ArgAt<HttpContext>(0);
                    ForwardedTo = call.ArgAt<string>(1);
                    CompletedOnClientCancel = call.ArgAt<StreamingHttpTransformer>(3).CompleteUpstreamOnClientCancel;

                    // What the transformer would put on the wire for this backend.
                    using var outbound = new HttpRequestMessage(HttpMethod.Post, ForwardedTo)
                    {
                        Content = new StreamContent(context.Request.Body),
                    };
                    await call.ArgAt<StreamingHttpTransformer>(3)
                        .TransformRequestAsync(context, outbound, ForwardedTo + "/", CancellationToken.None);
                    context.Request.Body.Position = 0;
                    ForwardedBody = await outbound.Content!.ReadAsStringAsync();

                    context.Response.StatusCode = _upstreamStatus;
                    return _forwardResult;
                });

            var services = new ServiceCollection().AddSingleton(_grants).BuildServiceProvider();

            return new ModelRouterMiddleware(
                _ => Task.CompletedTask,
                registry,
                health,
                services.GetRequiredService<IServiceScopeFactory>(),
                authState,
                new OpenAiErrorResponseWriter(),
                requestTracker,
                Substitute.For<IRecentRequestStore>(),
                Substitute.For<IUsageRecorder>(),
                Metrics,
                new ModelCircuitBreakerRegistry(options, Metrics),
                new BulkheadRegistry(options, Metrics),
                rateLimitResolver,
                Substitute.For<IDistributedRateLimitStore>(),
                forwarder,
                options,
                tokens,
                Budget,
                Substitute.For<IGatewayErrorRecorder>(),
                NullLogger<ModelRouterMiddleware>.Instance,
                spendCaps: SpendCaps);
        }
    }
}
