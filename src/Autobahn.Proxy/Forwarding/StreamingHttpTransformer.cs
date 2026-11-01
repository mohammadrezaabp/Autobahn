using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Autobahn.Proxy.Parsing;
using Autobahn.Proxy.Routing;
using Yarp.ReverseProxy.Forwarder;

namespace Autobahn.Proxy.Forwarding;

public sealed class StreamingHttpTransformer : HttpTransformer
{
    private readonly bool _isStreaming;
    private readonly string? _clientModelName;
    private readonly string _canonicalModelId;
    private readonly InferenceUsageCapture? _usageCapture;
    private readonly bool _stripClientAuthHeaders;
    private readonly string? _upstreamBearerToken;
    private readonly JsonValueRange? _modelValueRange;

    /// <param name="modelValueRange">
    /// Where the client's <c>model</c> value sits in the buffered request body, as reported by
    /// <see cref="InferenceRequestParser"/>. Supplying it lets an alias be rewritten by splicing
    /// bytes; when it is absent the transformer recovers it with one more streaming scan rather than
    /// falling back to materialising the body.
    /// </param>
    public StreamingHttpTransformer(
        bool isStreaming,
        string? clientModelName,
        string canonicalModelId,
        InferenceUsageCapture? usageCapture = null,
        bool stripClientAuthHeaders = true,
        string? upstreamBearerToken = null,
        JsonValueRange? modelValueRange = null)
    {
        _isStreaming = isStreaming;
        _clientModelName = clientModelName;
        _canonicalModelId = canonicalModelId;
        _usageCapture = usageCapture;
        _stripClientAuthHeaders = stripClientAuthHeaders;
        _upstreamBearerToken = upstreamBearerToken;
        _modelValueRange = modelValueRange;
    }

    /// <summary>
    /// Whether the upstream exchange outlives the client: when the client goes away, the forwarder
    /// stops writing to it but reads the upstream response to its end, so its usage is captured.
    /// </summary>
    /// <remarks>
    /// For a paid provider. It charges for every request it accepted whether or not anyone reads the
    /// answer, so cancelling the upstream when the client leaves saves nothing and loses the only
    /// record of what was charged: DeepSeek billed requests a client timed out on while the gateway
    /// recorded no usage for them at all. The read stays bound by the usual upstream deadlines.
    /// A self-hosted backend is cancelled as before, because there stopping does save the work.
    /// </remarks>
    public bool CompleteUpstreamOnClientCancel { get; init; }

    /// <summary>
    /// Whether a streamed request asks the upstream for its usage
    /// (<c>stream_options.include_usage</c>), for a provider that sends it only when asked.
    /// </summary>
    /// <remarks>
    /// Without it, every stream at OpenAI ends with no usage and is billed from an estimate: output
    /// from the number of chunks (reasoning tokens, which never stream, not at all) and input from the
    /// request's bytes divided by four (cached input at the full price, an inline image as if it
    /// were text). When the client did not ask for the usage chunk itself, it is taken out of the
    /// response before the client sees it (<see cref="SseUsageChunkFilterStream"/>).
    /// </remarks>
    public bool RequestStreamUsage { get; init; }

    /// <summary>
    /// The <c>service_tier</c> a client's own value is replaced with; null leaves it alone.
    /// </summary>
    /// <remarks>
    /// For a provider that bills by tier but does not report what it charged: the model's price
    /// list holds the default tier's prices, so a client asking for a dearer tier would be charged
    /// one price by the provider and recorded at another.
    /// </remarks>
    public string? PinnedServiceTier { get; init; }

    /// <summary>
    /// The layout of the request body, as <see cref="InferenceRequestParser"/> reported it. Lets the
    /// body be rewritten without scanning it again; re-derived when absent.
    /// </summary>
    public InferenceRequestInfo? RequestInfo { get; init; }

    private bool _stripUsageChunk;

    private static ReadOnlySpan<byte> IncludeUsageObject => "{\"include_usage\":true}"u8;

    public override async ValueTask TransformRequestAsync(
        HttpContext httpContext,
        HttpRequestMessage proxyRequest,
        string destinationPrefix,
        CancellationToken cancellationToken)
    {
        // The transformer owns the outbound URI; the forwarder hands over a request without one.
        proxyRequest.RequestUri = InferenceDestinationBuilder.BuildOutboundUri(
            destinationPrefix,
            httpContext.Request.Path,
            httpContext.Request.QueryString);

        CopyAllowedRequestHeaders(httpContext.Request, proxyRequest);

        if (_stripClientAuthHeaders)
        {
            proxyRequest.Headers.Authorization = null;
            proxyRequest.Headers.Remove("Authorization");
            proxyRequest.Headers.Remove("X-API-Key");
        }

        if (!string.IsNullOrWhiteSpace(_upstreamBearerToken))
        {
            proxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _upstreamBearerToken);
        }

        var rewriteModel = _clientModelName is not null &&
            !string.Equals(_clientModelName, _canonicalModelId, StringComparison.OrdinalIgnoreCase);
        var askForUsage = RequestStreamUsage && _isStreaming;
        if ((!rewriteModel && !askForUsage && PinnedServiceTier is null) || !httpContext.Request.Body.CanSeek)
        {
            return;
        }

        var info = RequestInfo;
        if (info is null || (rewriteModel && _modelValueRange is null && info.Value.ModelValueRange is null))
        {
            info = await LocateLayoutAsync(httpContext.Request.Body, cancellationToken).ConfigureAwait(false);
        }

        if (info is not { } layout)
        {
            // Nothing to splice — forward the body untouched rather than guess at its shape.
            return;
        }

        var splices = new List<RequestBodySplice>(3);
        if (rewriteModel && (_modelValueRange ?? layout.ModelValueRange) is { } modelValueRange)
        {
            splices.Add(RequestBodySplice.JsonString(modelValueRange, _canonicalModelId));
        }

        if (askForUsage && StreamUsageSplice(layout) is { } usage)
        {
            splices.Add(usage);
        }

        if (PinnedServiceTier is { } tier && layout.ServiceTierValueRange is { } tierRange)
        {
            splices.Add(RequestBodySplice.JsonString(tierRange, tier));
        }

        if (splices.Count == 0)
        {
            return;
        }

        var rewritten = SplicedRequestContent.TryCreate(
            httpContext.Request.Body,
            splices,
            proxyRequest.Content?.Headers.ContentType);
        if (rewritten is not null)
        {
            proxyRequest.Content = rewritten;

            // Only once the request really asks for usage on the client's behalf.
            _stripUsageChunk = askForUsage && !layout.ClientRequestedStreamUsage;
        }
    }

    /// <summary>
    /// The change that makes the body ask for a stream's usage: a <c>stream_options</c> added, an
    /// <c>include_usage</c> added to the client's or set to <c>true</c>. Null when the client asked
    /// already, or its <c>stream_options</c> is in a shape no upstream would accept anyway.
    /// </summary>
    internal static RequestBodySplice? StreamUsageSplice(InferenceRequestInfo layout)
    {
        if (layout.StreamOptions is not { } options)
        {
            // The root holds at least the routed "model", so a property follows the insert.
            return RequestBodySplice.Insert(layout.RootStart + 1, "\"stream_options\":{\"include_usage\":true},"u8);
        }

        if (options.IncludeUsage || options.Unusable)
        {
            return null;
        }

        if (!options.IsObject)
        {
            return new RequestBodySplice(options.Value, IncludeUsageObject.ToArray());
        }

        if (options.IncludeUsageValueRange is { } valueRange)
        {
            return new RequestBodySplice(valueRange, "true"u8.ToArray());
        }

        return RequestBodySplice.Insert(
            options.Value.Start + 1,
            options.HasProperties ? "\"include_usage\":true,"u8 : "\"include_usage\":true"u8);
    }

    /// <summary>
    /// Re-derives the body's layout for callers that constructed the transformer without it. Costs
    /// one more bounded streaming scan — never a copy of the body.
    /// </summary>
    private static async Task<InferenceRequestInfo?> LocateLayoutAsync(
        Stream body,
        CancellationToken cancellationToken)
    {
        try
        {
            body.Position = 0;
            return await InferenceRequestParser.ParseAsync(body, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            if (body.CanSeek)
            {
                body.Position = 0;
            }
        }
    }

    public override async ValueTask<bool> TransformResponseAsync(
        HttpContext httpContext,
        HttpResponseMessage? proxyResponse,
        CancellationToken cancellationToken)
    {
        if (_usageCapture is not null && proxyResponse?.Content is not null)
        {
            await PrepareUsageCapturingContentAsync(proxyResponse, cancellationToken).ConfigureAwait(false);
        }
        else if (_stripUsageChunk && proxyResponse?.Content is not null)
        {
            await ReplaceContentAsync(proxyResponse, s => new SseUsageChunkFilterStream(s), cancellationToken)
                .ConfigureAwait(false);
        }

        // Response headers and body copy are handled by InferenceHttpForwarder for both
        // streaming and non-streaming responses.
        // Calling YARP's default response transform here can re-apply transfer headers
        // before we copy the response body ourselves.
        _ = httpContext;
        _ = proxyResponse;
        _ = cancellationToken;
        return true;
    }

    /// <summary>
    /// Wraps the upstream body so usage can be read off it as it streams through to the client.
    /// </summary>
    /// <remarks>
    /// <para>The replacement content carries the original content headers (minus
    /// <c>Content-Length</c>, which the wrapper cannot honour and Kestrel replaces with chunking).
    /// Carrying only <c>Content-Type</c>, as this once did, silently dropped <c>Content-Encoding</c>,
    /// <c>Content-Language</c> and friends on the way to the client.</para>
    ///
    /// <para>The gateway never sends <c>Accept-Encoding</c> upstream, but some CDNs and proxies
    /// compress regardless. A body under a non-identity <c>Content-Encoding</c> is not JSON/SSE on
    /// the wire, so parsing it for usage would only ever fail (and be counted as a parse failure of a
    /// backend that did nothing wrong). It is passed through untouched and reported through the
    /// capture's failure hook so it stays visible in metrics.</para>
    /// </remarks>
    private async Task PrepareUsageCapturingContentAsync(
        HttpResponseMessage proxyResponse,
        CancellationToken cancellationToken)
    {
        if (HasNonIdentityContentEncoding(proxyResponse.Content!.Headers))
        {
            _usageCapture!.OnBodyNotParseable();
            return;
        }

        await ReplaceContentAsync(proxyResponse, Wrap, cancellationToken).ConfigureAwait(false);

        Stream Wrap(Stream originalStream)
        {
            Stream capturingStream = _isStreaming
                ? new UsageCapturingStream(
                    originalStream,
                    // Stats carry how much was actually streamed, so a body cut short before its usage
                    // frame can still be billed from an estimate rather than recorded as zero. SSE usage
                    // arrives in the final frames, so only the tail is meaningful here.
                    (_, tail, stats) => _usageCapture!.CaptureFromSseText(
                        Encoding.UTF8.GetString(tail), stats),
                    isStreaming: true,
                    onCaptureFailed: _usageCapture!.OnCaptureFailed)
                : new UsageCapturingStream(
                    originalStream,
                    // Head for bodies that fit (exact parse), tail as the fallback for bodies that do
                    // not — the trailing usage object is what makes large responses billable at all.
                    (head, tail, stats) => _usageCapture!.CaptureFromJsonBody(head, tail, stats),
                    isStreaming: false,
                    onCaptureFailed: _usageCapture!.OnCaptureFailed);

            // The capture reads the usage chunk beneath the filter that keeps it from the client.
            return _stripUsageChunk ? new SseUsageChunkFilterStream(capturingStream) : capturingStream;
        }
    }

    private static async Task ReplaceContentAsync(
        HttpResponseMessage proxyResponse,
        Func<Stream, Stream> wrap,
        CancellationToken cancellationToken)
    {
        var originalHeaders = proxyResponse.Content!.Headers;
        if (HasNonIdentityContentEncoding(originalHeaders))
        {
            return;
        }

        var originalStream = await proxyResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var replacement = new StreamContent(wrap(originalStream));
        foreach (var header in originalHeaders)
        {
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        proxyResponse.Content = replacement;
    }

    private static bool HasNonIdentityContentEncoding(HttpContentHeaders headers)
    {
        foreach (var encoding in headers.ContentEncoding)
        {
            if (!string.IsNullOrWhiteSpace(encoding) &&
                !string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Client request headers the gateway relays to the upstream.
    /// </summary>
    /// <remarks>
    /// <para>An allowlist, not a copy-everything. The forwarder builds a fresh request and this
    /// transformer never chained to <c>base.TransformRequestAsync</c>, so previously <em>no</em>
    /// client header reached the upstream at all. That silently broke any provider feature carried
    /// by a header — <c>OpenAI-Beta</c>, <c>OpenAI-Organization</c>, provider API versions — with no
    /// diagnostic beyond the feature not working.</para>
    ///
    /// <para>Deliberately excluded: anything that would let a client influence routing, identity or
    /// framing. <c>Authorization</c> and <c>X-API-Key</c> are the gateway's own credential to
    /// replace; <c>Host</c>, <c>Content-Length</c> and <c>Transfer-Encoding</c> belong to the new
    /// request; <c>Accept-Encoding</c> is omitted so upstream bodies arrive uncompressed and stay
    /// parseable for usage capture; forwarding headers (<c>X-Forwarded-*</c>) are not relayed
    /// because a client could otherwise spoof them.</para>
    /// </remarks>
    private static readonly string[] ForwardableRequestHeaders =
    [
        "Accept",
        "Accept-Language",
        "User-Agent",
        "OpenAI-Beta",
        "OpenAI-Organization",
        "OpenAI-Project",
        "anthropic-version",
        "anthropic-beta",
        "X-Stainless-Lang",
        "X-Stainless-Package-Version",
        "X-Stainless-Runtime",
        "X-Stainless-Runtime-Version",
    ];

    private static void CopyAllowedRequestHeaders(HttpRequest source, HttpRequestMessage destination)
    {
        foreach (var name in ForwardableRequestHeaders)
        {
            if (!source.Headers.TryGetValue(name, out var values) || values.Count == 0)
            {
                continue;
            }

            destination.Headers.TryAddWithoutValidation(name, values.ToArray());
        }
    }

}
