using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;
using Autobahn.Proxy.Forwarding;
using Yarp.ReverseProxy.Forwarder;

namespace Autobahn.Proxy.Tests.Forwarding;

/// <summary>
/// A paid provider bills a request whether or not the client stays for the answer. DeepSeek billed
/// requests workspace's client timed out on while the gateway, having cancelled the upstream with
/// the client, recorded no usage for them at all: about half of a week's output tokens.
/// </summary>
public sealed class InferenceHttpForwarderClientCancelTests
{
    private static readonly InferenceForwardTimeouts Timeouts = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

    private const string UsageJson =
        """{"prompt_tokens":1200,"completion_tokens":3400,"prompt_cache_hit_tokens":1100,"prompt_cache_miss_tokens":100}""";

    [Fact]
    public async Task NonStreaming_ClientLeavesBeforeTheAnswer_UpstreamIsReadToTheEnd_AndItsUsageRecorded()
    {
        var upstream = new SlowJsonUpstream(TimeSpan.FromMilliseconds(300));
        var harness = new Harness(upstream, isStreaming: false, completeUpstream: true);

        using var client = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var error = await harness.SendAsync(client.Token);

        error.Should().Be(ForwarderError.RequestCanceled, "the client did leave");
        upstream.WasCancelled.Should().BeFalse("the upstream must not be cancelled with the client");
        var usage = harness.Recorded.Should().ContainSingle().Subject;
        usage.TokenSource.Should().Be(UsageTokenSource.Split);
        usage.PromptTokens.Should().Be(1200);
        usage.CachedPromptTokens.Should().Be(1100);
        usage.CompletionTokens.Should().Be(3400);
        harness.Capture.HasEnqueuedUsage.Should().BeTrue();
    }

    /// <summary>A self-hosted backend is stopped with the client, as before: there stopping saves the work.</summary>
    [Fact]
    public async Task NonStreaming_ClientLeaves_WithoutCompletion_CancelsTheUpstream_AndRecordsNothing()
    {
        var upstream = new SlowJsonUpstream(TimeSpan.FromMilliseconds(300));
        var harness = new Harness(upstream, isStreaming: false, completeUpstream: false);

        using var client = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var error = await harness.SendAsync(client.Token);

        error.Should().Be(ForwarderError.RequestCanceled);
        upstream.WasCancelled.Should().BeTrue();
        harness.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task Streaming_ClientDropsMidStream_TheRestIsRead_AndTheProvidersUsageRecorded()
    {
        var harness = new Harness(new SseUpstream(), isStreaming: true, completeUpstream: true);
        harness.Context.Response.Body = new ThrowsAfterFirstWrite();

        var error = await harness.SendAsync(CancellationToken.None);

        error.Should().Be(ForwarderError.RequestCanceled, "the client's write failed: it went away");
        var usage = harness.Recorded.Should().ContainSingle().Subject;
        usage.TokenSource.Should().Be(UsageTokenSource.Split, "the terminal usage frame was read, not estimated");
        usage.CompletionTokens.Should().Be(3400);
        usage.CachedPromptTokens.Should().Be(1100);
    }

    [Fact]
    public async Task Streaming_ClientDropsMidStream_WithoutCompletion_IsOnlyEstimated()
    {
        var harness = new Harness(new SseUpstream(), isStreaming: true, completeUpstream: false);
        harness.Context.Response.Body = new ThrowsAfterFirstWrite();

        var error = await harness.SendAsync(CancellationToken.None);

        error.Should().Be(ForwarderError.RequestCanceled);
        harness.Recorded.Should().ContainSingle().Which.TokenSource.Should().Be(UsageTokenSource.Estimated);
    }

    [Fact]
    public async Task Completing_DoesNotChangeAServedRequest()
    {
        var harness = new Harness(new SlowJsonUpstream(TimeSpan.Zero), isStreaming: false, completeUpstream: true);

        var error = await harness.SendAsync(CancellationToken.None);

        error.Should().Be(ForwarderError.None);
        ((MemoryStream)harness.Context.Response.Body).ToArray().Should().NotBeEmpty();
        harness.Recorded.Should().ContainSingle().Which.CompletionTokens.Should().Be(3400);
    }

    private sealed class Harness
    {
        private readonly InferenceHttpForwarder _forwarder;
        private readonly StreamingHttpTransformer _transformer;
        private readonly bool _isStreaming;

        public Harness(HttpMessageHandler upstream, bool isStreaming, bool completeUpstream)
        {
            _isStreaming = isStreaming;
            var recorder = Substitute.For<IUsageRecorder>();
            recorder.Enqueue(Arg.Do<UsageEvent>(Recorded.Add)).Returns(true);

            Capture = new InferenceUsageCapture(
                recorder,
                Substitute.For<IGatewayMetricsCollector>(),
                "deepseek-v4-flash",
                "req-1",
                DateTimeOffset.UtcNow,
                tenant: null,
                requestBodyBytes: 4096,
                provider: "deepseek");
            _transformer = new StreamingHttpTransformer(isStreaming, "deepseek-v4-flash", "deepseek-v4-flash", Capture)
            {
                CompleteUpstreamOnClientCancel = completeUpstream,
            };
            _forwarder = new InferenceHttpForwarder(
                new SingleHandlerFactory(upstream),
                Substitute.For<IGatewayMetricsCollector>(),
                NullLogger<InferenceHttpForwarder>.Instance);

            var body = Encoding.UTF8.GetBytes(isStreaming
                ? """{"model":"deepseek-v4-flash","stream":true}"""
                : """{"model":"deepseek-v4-flash"}""");
            Context = new DefaultHttpContext
            {
                Request =
                {
                    Method = HttpMethods.Post,
                    Path = "/v1/chat/completions",
                    Body = new MemoryStream(body),
                    ContentType = "application/json",
                    ContentLength = body.Length,
                },
                Response = { Body = new MemoryStream() },
            };
            Context.Request.EnableBuffering();
        }

        public DefaultHttpContext Context { get; }

        public InferenceUsageCapture Capture { get; }

        public List<UsageEvent> Recorded { get; } = [];

        public Task<ForwarderError> SendAsync(CancellationToken clientToken) =>
            _forwarder.SendAsync(Context, "https://api.deepseek.com", "sk-upstream", _transformer, _isStreaming, Timeouts, clientToken);
    }

    /// <summary>Answers after a delay, as a reasoning model does; notes whether it was cancelled meanwhile.</summary>
    private sealed class SlowJsonUpstream(TimeSpan delay) : HttpMessageHandler
    {
        public bool WasCancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }

            var json = """{"id":"x","choices":[{"index":0,"message":{"role":"assistant","content":"hi"}}],"usage":""" + UsageJson + "}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>Content frames, then the terminal usage frame and [DONE].</summary>
    private sealed class SseUpstream : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sse = new StringBuilder();
            for (var i = 0; i < 5; i++)
            {
                sse.Append("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"tok").Append(i).Append("\"}}]}\n\n");
            }

            sse.Append("data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":").Append(UsageJson).Append("}\n\n");
            sse.Append("data: [DONE]\n\n");

            // One frame per read, so the client's write fails while most of the stream is still upstream.
            var content = new StreamContent(new FramePerReadStream(Encoding.UTF8.GetBytes(sse.ToString())));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class FramePerReadStream : MemoryStream
    {
        private readonly byte[] _data;

        public FramePerReadStream(byte[] data)
            : base(data)
        {
            _data = data;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = _data.AsSpan((int)Position);
            var end = remaining.IndexOf("\n\n"u8);
            var take = Math.Min(buffer.Length, end < 0 ? remaining.Length : end + 2);
            return base.ReadAsync(buffer[..take], cancellationToken);
        }
    }

    /// <summary>A client that disconnects after the first chunk it is sent.</summary>
    private sealed class ThrowsAfterFirstWrite : MemoryStream
    {
        private bool _written;

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_written)
            {
                throw new IOException("Simulated client disconnect.");
            }

            _written = true;
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
