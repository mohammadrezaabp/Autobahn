using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Autobahn.Application.Errors;
using Autobahn.Domain.Identity;
using Autobahn.Domain.RateLimiting;
using Autobahn.Proxy.Diagnostics;
using Autobahn.Proxy.Middleware;

namespace Autobahn.Proxy.Tests.Middleware;

public sealed class InferenceAccessLogMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ForwardedRequest_WritesOneLineWithWhoWhatAndHowItEnded()
    {
        var logger = new CapturingLogger();
        var middleware = new InferenceAccessLogMiddleware(
            context =>
            {
                context.Items[TenantContextKeys.HttpContextItemKey] = new TenantContext
                {
                    TenantId = Guid.NewGuid().ToString(),
                    TenantSlug = "easy",
                    ApiKeyId = Guid.NewGuid().ToString(),
                    ApiKeyLabel = "easy prod 1",
                };
                InferenceAccessLog.SetLimitDecision(context, admitted: true, scope: null);
                InferenceAccessLog.SetModel(context, "qwen3");
                InferenceAccessLog.SetUpstreamStatus(context, 200);
                InferenceAccessLog.SetOutcome(context, "success");
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            logger);
        var context = CreateContext("/v1/chat/completions");
        context.Items[RequestIdKeys.HttpContextItemKey] = "req_abc";

        await middleware.InvokeAsync(context);

        logger.Lines.Should().ContainSingle();
        var line = logger.Lines[0];
        line.Level.Should().Be(LogLevel.Information);
        line.Message.Should().StartWith("inference_request request_id=req_abc tenant=\"easy\" key=\"easy prod 1\" model=\"qwen3\" ");
        line.Message.Should().Contain("status=200 outcome=success limit_decision=admitted limit_scope=none upstream_status=200 ");
        line.Properties["Tenant"].Should().Be("easy");
        line.Properties["UpstreamStatus"].Should().Be("200");
    }

    [Fact]
    public async Task InvokeAsync_RefusedByTheRateLimiter_NamesTheScopeAndHasNoUpstreamStatus()
    {
        var logger = new CapturingLogger();
        var middleware = new InferenceAccessLogMiddleware(
            context =>
            {
                InferenceAccessLog.SetLimitDecision(context, admitted: false, RateLimitScope.ApiKey);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers[GatewayHeaders.ErrorCode] = "rate_limit_exceeded";
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(CreateContext("/v1/embeddings"));

        var message = logger.Lines.Should().ContainSingle().Subject.Message;
        message.Should().Contain("tenant=\"anonymous\" key=\"(none)\" model=\"unknown\" ");
        message.Should().Contain("status=429 outcome=rate_limit_exceeded limit_decision=refused limit_scope=api_key upstream_status=none ");
    }

    /// <summary>A stream cap refuses after the rate stage admitted: the line reads as refused.</summary>
    [Fact]
    public async Task InvokeAsync_LaterRefusalReplacesAnEarlierAdmission()
    {
        var logger = new CapturingLogger();
        var middleware = new InferenceAccessLogMiddleware(
            context =>
            {
                InferenceAccessLog.SetLimitDecision(context, admitted: true, scope: null);
                InferenceAccessLog.SetLimitDecision(context, admitted: false, RateLimitScope.Tenant);
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(CreateContext("/v1/chat/completions"));

        logger.Lines[0].Message.Should().Contain("limit_decision=refused limit_scope=tenant ");
    }

    [Fact]
    public async Task InvokeAsync_QuoteInsideAKeyLabel_CannotEndTheValueEarly()
    {
        var logger = new CapturingLogger();
        var middleware = new InferenceAccessLogMiddleware(
            context =>
            {
                context.Items[TenantContextKeys.HttpContextItemKey] = new TenantContext
                {
                    TenantId = Guid.NewGuid().ToString(),
                    TenantSlug = "easy",
                    ApiKeyId = Guid.NewGuid().ToString(),
                    ApiKeyLabel = "the \"main\" key",
                };
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(CreateContext("/v1/chat/completions"));

        logger.Lines[0].Message.Should().Contain(" key=\"the 'main' key\" ");
    }

    [Fact]
    public async Task InvokeAsync_WhenThePipelineThrows_StillWritesTheLine()
    {
        var logger = new CapturingLogger();
        var middleware = new InferenceAccessLogMiddleware(_ => throw new InvalidOperationException("boom"), logger);

        var act = () => middleware.InvokeAsync(CreateContext("/v1/chat/completions"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Lines.Should().ContainSingle();
    }

    [Theory]
    [InlineData("/admin/api/summary")]
    [InlineData("/v1/models")]
    [InlineData("/metrics")]
    public async Task InvokeAsync_NotAnInferenceRequest_WritesNothing(string path)
    {
        var logger = new CapturingLogger();
        var middleware = new InferenceAccessLogMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(CreateContext(path, HttpMethods.Get));

        logger.Lines.Should().BeEmpty();
    }

    private static DefaultHttpContext CreateContext(string path, string? method = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method ?? HttpMethods.Post;
        context.Request.Path = path;
        return context;
    }

    private sealed class CapturingLogger : ILogger<InferenceAccessLogMiddleware>
    {
        public List<(LogLevel Level, string Message, Dictionary<string, object?> Properties)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    properties[pair.Key] = pair.Value;
                }
            }

            Lines.Add((logLevel, formatter(state, exception), properties));
        }
    }
}
