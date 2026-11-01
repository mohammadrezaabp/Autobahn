using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Autobahn.Application.Abstractions;
using Autobahn.Proxy.Forwarding;
using Autobahn.Proxy.Parsing;

namespace Autobahn.Proxy.Tests.Forwarding;

/// <summary>
/// A stream at a provider that sends its usage only when asked (OpenAI) asks for it, so it is billed
/// from the provider's own counts rather than a chunk count and a byte estimate; and a client that
/// did not ask for the usage chunk never sees it.
/// </summary>
public sealed class StreamUsageRequestTests
{
    [Theory]
    [InlineData(
        """{"model":"m","stream":true}""",
        """{"stream_options":{"include_usage":true},"model":"m","stream":true}""")]
    [InlineData(
        """{"model":"m","stream":true,"stream_options":{}}""",
        """{"model":"m","stream":true,"stream_options":{"include_usage":true}}""")]
    [InlineData(
        """{"model":"m","stream":true,"stream_options":{"include_obfuscation":false}}""",
        """{"model":"m","stream":true,"stream_options":{"include_usage":true,"include_obfuscation":false}}""")]
    [InlineData(
        """{"model":"m","stream":true,"stream_options":{"include_usage":false}}""",
        """{"model":"m","stream":true,"stream_options":{"include_usage":true}}""")]
    [InlineData(
        """{"model":"m","stream":true,"stream_options":null}""",
        """{"model":"m","stream":true,"stream_options":{"include_usage":true}}""")]
    public async Task Streamed_AsksForUsage(string body, string expected)
    {
        var (_, request, _, _) = await RunAsync(body, requestStreamUsage: true);

        var forwarded = await request.Content!.ReadAsStringAsync();
        forwarded.Should().Be(expected);
        request.Content.Headers.ContentLength.Should().Be(Encoding.UTF8.GetByteCount(expected));
        using var _ = JsonDocument.Parse(forwarded);
    }

    [Fact]
    public async Task ClientAskedAlready_BodyUnchanged()
    {
        var (_, request, _, _) = await RunAsync(
            """{"model":"m","stream":true,"stream_options":{"include_usage":true}}""",
            requestStreamUsage: true);

        request.Content.Should().BeOfType<StreamContent>();
    }

    [Fact]
    public async Task NotStreamed_BodyUnchanged()
    {
        var (_, request, _, _) = await RunAsync("""{"model":"m"}""", requestStreamUsage: true, isStreaming: false);

        request.Content.Should().BeOfType<StreamContent>();
    }

    [Fact]
    public async Task ProviderNotAsked_BodyUnchanged()
    {
        var (_, request, _, _) = await RunAsync("""{"model":"m","stream":true}""", requestStreamUsage: false);

        request.Content.Should().BeOfType<StreamContent>();
    }

    [Fact]
    public async Task AliasAndUsageAndServiceTier_AllSplicedTogether()
    {
        var (_, request, _, _) = await RunAsync(
            """{"model":"alias","stream":true,"service_tier":"priority"}""",
            requestStreamUsage: true,
            clientModelName: "alias",
            pinnedServiceTier: "default");

        (await request.Content!.ReadAsStringAsync()).Should().Be(
            """{"stream_options":{"include_usage":true},"model":"m","stream":true,"service_tier":"default"}""");
    }

    [Theory]
    [InlineData("""{"model":"m","service_tier":"priority"}""", """{"model":"m","service_tier":"default"}""")]
    [InlineData("""{"model":"m","service_tier":"flex"}""", """{"model":"m","service_tier":"default"}""")]
    public async Task ServiceTier_PinnedToTheTierThePriceHolds(string body, string expected)
    {
        var (_, request, _, _) = await RunAsync(body, requestStreamUsage: false, isStreaming: false, pinnedServiceTier: "default");

        (await request.Content!.ReadAsStringAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task ServiceTier_AbsentIsLeftAbsent()
    {
        var (_, request, _, _) = await RunAsync("""{"model":"m"}""", requestStreamUsage: false, isStreaming: false, pinnedServiceTier: "default");

        request.Content.Should().BeOfType<StreamContent>();
    }

    [Fact]
    public async Task UsageAskedForTheClient_UsageChunkCapturedButNotForwarded()
    {
        const string upstream =
            "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}],\"usage\":null}\n\n" +
            "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":7,\"total_tokens\":18}}\n\n" +
            "data: [DONE]\n\n";

        var (context, _, transformer, recorder) = await RunAsync("""{"model":"m","stream":true}""", requestStreamUsage: true);
        var (client, usage) = await RespondAsync(context, transformer, recorder, upstream);

        client.Should().NotContain("\"choices\":[]");
        client.Should().Contain("\"content\":\"hi\"").And.EndWith("data: [DONE]\n\n");
        usage.PromptTokens.Should().Be(11);
        usage.CompletionTokens.Should().Be(7);
    }

    [Fact]
    public async Task ClientAskedForUsage_UsageChunkForwarded()
    {
        const string upstream =
            "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":7}}\n\n" +
            "data: [DONE]\n\n";

        var (context, _, transformer, recorder) = await RunAsync(
            """{"model":"m","stream":true,"stream_options":{"include_usage":true}}""",
            requestStreamUsage: true);
        var (client, _) = await RespondAsync(context, transformer, recorder, upstream);

        client.Should().Be(upstream);
    }

    [Fact]
    public async Task FilterStream_EventSplitAcrossReads_StillFiltered()
    {
        const string sse =
            "data: {\"choices\":[{\"delta\":{}}]}\r\n\r\n" +
            "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":1}}\r\n\r\n" +
            ": keep-alive\n\n" +
            "data: [DONE]\n\n";
        var bytes = Encoding.UTF8.GetBytes(sse);

        await using var filter = new SseUsageChunkFilterStream(new TrickleStream(bytes, chunk: 3));
        using var reader = new StreamReader(filter);
        var result = await reader.ReadToEndAsync();

        result.Should().Be(
            "data: {\"choices\":[{\"delta\":{}}]}\r\n\r\n" +
            ": keep-alive\n\n" +
            "data: [DONE]\n\n");
    }

    [Fact]
    public async Task FilterStream_UnterminatedTail_PassedThrough()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"error\":{\"message\":\"bad\"}}");

        await using var filter = new SseUsageChunkFilterStream(new MemoryStream(bytes));
        using var reader = new StreamReader(filter);

        (await reader.ReadToEndAsync()).Should().Be("{\"error\":{\"message\":\"bad\"}}");
    }

    [Theory]
    [InlineData("""{"model":"m","stream_options":{"include_usage":true,"include_usage":true}}""")]
    [InlineData("""{"model":"m","stream_options":{},"stream_options":{}}""")]
    [InlineData("""{"model":"m","service_tier":"default","service_tier":"priority"}""")]
    public async Task Parser_DuplicateRewrittenProperty_Rejected(string body)
    {
        var act = () => InferenceRequestParser.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(body)));

        await act.Should().ThrowAsync<JsonException>();
    }

    private static async Task<(DefaultHttpContext Context, HttpRequestMessage Request, StreamingHttpTransformer Transformer, CapturingRecorder Recorder)> RunAsync(
        string body,
        bool requestStreamUsage,
        bool isStreaming = true,
        string clientModelName = "m",
        string? pinnedServiceTier = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/chat/completions";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        var parsed = await InferenceRequestParser.ParseAsync(context.Request.Body, CancellationToken.None);
        context.Request.Body.Position = 0;

        var recorder = new CapturingRecorder();
        var transformer = new StreamingHttpTransformer(
            isStreaming,
            clientModelName,
            canonicalModelId: "m",
            usageCapture: new InferenceUsageCapture(
                recorder,
                Substitute.For<IGatewayMetricsCollector>(),
                "m",
                "req-1",
                DateTimeOffset.UtcNow,
                tenant: null,
                requestBodyBytes: body.Length),
            modelValueRange: parsed.ModelValueRange)
        {
            RequestInfo = parsed,
            RequestStreamUsage = requestStreamUsage,
            PinnedServiceTier = pinnedServiceTier,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "http://upstream/v1/chat/completions")
        {
            Content = new StreamContent(context.Request.Body),
        };
        await transformer.TransformRequestAsync(context, request, "http://upstream", CancellationToken.None);
        return (context, request, transformer, recorder);
    }

    private static async Task<(string Client, Autobahn.Domain.Models.UsageEvent Usage)> RespondAsync(
        DefaultHttpContext context,
        StreamingHttpTransformer transformer,
        CapturingRecorder recorder,
        string upstream)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(upstream, Encoding.UTF8, "text/event-stream"),
        };
        await transformer.TransformResponseAsync(context, response, CancellationToken.None);

        string client;
        await using (var body = await response.Content.ReadAsStreamAsync())
        using (var reader = new StreamReader(body))
        {
            client = await reader.ReadToEndAsync();
        }

        response.Dispose();
        return (client, recorder.Last!);
    }

    private sealed class CapturingRecorder : IUsageRecorder
    {
        public Autobahn.Domain.Models.UsageEvent? Last { get; private set; }

        public bool Enqueue(Autobahn.Domain.Models.UsageEvent usageEvent)
        {
            Last = usageEvent;
            return true;
        }
    }

    /// <summary>Hands out a few bytes per read, so events straddle reads.</summary>
    private sealed class TrickleStream(byte[] data, int chunk) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, chunk));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);
    }
}
