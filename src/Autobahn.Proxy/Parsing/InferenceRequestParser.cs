using System.Buffers;
using System.Text.Json;

namespace Autobahn.Proxy.Parsing;
/// <summary>
/// Reads the handful of top-level scalars the gateway routes on out of an inference request body.
/// </summary>
/// <remarks>
/// <para>Deliberately a forward-only <see cref = "Utf8JsonReader"/> scan rather than a
/// <see cref = "JsonDocument"/> parse. <c>JsonDocument.ParseAsync</c> materialises the whole document
/// to reach three scalars, and against the buffered request body it does so badly: an
/// <c>EnableBuffering</c> stream reports <c>Length == 0</c> until it has been read, so the document
/// reader cannot size its rent up front and instead grows by doubling — renting, copying and
/// zero-clearing progressively larger Large Object Heap arrays. Measured at ~4.8x the body size in
/// allocations per parse, on a path that ran twice per request. This scan holds one pooled buffer
/// that only ever grows to the largest single JSON token in the body.</para>
///
/// <para>The stream is drained to EOF even after the root object closes. Callers forward the same
/// buffered body afterwards and take its <c>Length</c> as the outbound <c>Content-Length</c>, so
/// leaving unread bytes behind would truncate the forwarded request. <c>JsonDocument.ParseAsync</c>
/// read to EOF too, so this preserves the previous contract exactly.</para>
/// </remarks>
public static class InferenceRequestParser
{
    private const int InitialBufferBytes = 8 * 1024;
    private static ReadOnlySpan<byte> ModelPropertyName => "model"u8;
    private static ReadOnlySpan<byte> StreamPropertyName => "stream"u8;
    private static ReadOnlySpan<byte> MaxTokensPropertyName => "max_tokens"u8;
    private static ReadOnlySpan<byte> MaxCompletionTokensPropertyName => "max_completion_tokens"u8;
    private static ReadOnlySpan<byte> StreamOptionsPropertyName => "stream_options"u8;
    private static ReadOnlySpan<byte> IncludeUsagePropertyName => "include_usage"u8;
    private static ReadOnlySpan<byte> ServiceTierPropertyName => "service_tier"u8;

    public static async Task<InferenceRequestInfo> ParseAsync(Stream body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var buffer = ArrayPool<byte>.Shared.Rent(InitialBufferBytes);
        try
        {
            var scan = new TopLevelScan();
            var dataLength = 0;
            var consumed = 0;
            long bufferStartOffset = 0;
            var readerState = new JsonReaderState();
            var reachedEnd = false;
            while (true)
            {
                if (!reachedEnd)
                {
                    if (dataLength == buffer.Length)
                    {
                        // The previous pass consumed nothing, so a single token is larger than the
                        // buffer. This is the only condition under which the buffer grows.
                        Grow(ref buffer, dataLength);
                    }

                    var read = await body.ReadAsync(buffer.AsMemory(dataLength), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        reachedEnd = true;
                    }
                    else
                    {
                        dataLength += read;
                    }
                }

                var reader = new Utf8JsonReader(buffer.AsSpan(0, dataLength), reachedEnd, readerState);
                ScanTopLevel(ref reader, scan, bufferStartOffset);
                readerState = reader.CurrentState;
                consumed = (int)reader.BytesConsumed;
                if (scan.RootClosed)
                {
                    break;
                }

                if (reachedEnd)
                {
                    throw new JsonException("Request body is not a complete JSON object.");
                }

                if (consumed > 0)
                {
                    buffer.AsSpan(consumed, dataLength - consumed).CopyTo(buffer);
                    dataLength -= consumed;
                    bufferStartOffset += consumed;
                }
            }

            // Anything after the JSON document still belongs to the request body the gateway will
            // forward, so it is pulled into the caller's buffering stream before returning — leaving
            // it unread would make the stream's Length short and truncate the forwarded request.
            //
            // It must also be nothing but whitespace. A document-based parse rejected trailing
            // content, and relaxing that here would forward a body the gateway had declared valid but
            // no upstream would accept.
            RejectTrailingContent(buffer.AsSpan(consumed, dataLength - consumed));
            if (!reachedEnd)
            {
                int read;
                while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    RejectTrailingContent(buffer.AsSpan(0, read));
                }
            }

            return new InferenceRequestInfo(scan.Model, scan.Stream, // OpenAI "max_tokens" (legacy) takes precedence over "max_completion_tokens" (newer),
            // matching the order the previous document-based parser tried them in.
            scan.MaxTokens ?? scan.MaxCompletionTokens, scan.ModelValueRange)
            {
                RootStart = scan.RootStart,
                StreamOptions = scan.StreamOptionsSeen ? new StreamOptionsLayout(new JsonValueRange(scan.StreamOptionsStart, scan.StreamOptionsEnd), scan.StreamOptionsIsObject, scan.StreamOptionsHasProperties, scan.IncludeUsage, scan.IncludeUsageValueRange, scan.StreamOptionsUnusable) : null,
                ServiceTierValueRange = scan.ServiceTierValueRange,
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void RejectTrailingContent(ReadOnlySpan<byte> trailing)
    {
        foreach (var b in trailing)
        {
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
            {
                throw new JsonException("Unexpected content after the top-level JSON object.");
            }
        }
    }

    private static void Grow(ref byte[] buffer, int dataLength)
    {
        if (buffer.Length >= Array.MaxLength / 2)
        {
            throw new JsonException("A single JSON token in the request body is too large to parse.");
        }

        var grown = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
        buffer.AsSpan(0, dataLength).CopyTo(grown);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer = grown;
    }

    /// <summary>
    /// Advances the reader as far as the current buffer allows, recording the top-level properties
    /// the gateway cares about. Nested tokens are walked past rather than skipped, so no value ever
    /// has to fit in the buffer in its entirety — only individual tokens do.
    /// </summary>
    private static void ScanTopLevel(ref Utf8JsonReader reader, TopLevelScan scan, long bufferStartOffset)
    {
        while (reader.Read())
        {
            if (!scan.SawRootStart)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException("Request body must be a JSON object.");
                }

                scan.SawRootStart = true;
                scan.RootStart = bufferStartOffset + reader.TokenStartIndex;
                continue;
            }

            // A property name is always followed by its value, so a pending property claims this
            // token before anything else is considered.
            if (scan.Pending != TopLevelProperty.None)
            {
                ReadPendingValue(ref reader, scan, bufferStartOffset);
                scan.Pending = TopLevelProperty.None;
                continue;
            }

            if (scan.InStreamOptions)
            {
                ScanStreamOptions(ref reader, scan, bufferStartOffset);
                continue;
            }

            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0)
            {
                scan.RootClosed = true;
                return;
            }

            // Depth 1 is the root object's own properties. Anything deeper — a "model" key inside a
            // message, say — is not what the gateway routes on.
            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
            {
                continue;
            }

            scan.Pending = ClassifyProperty(ref reader, scan);
        }
    }

    /// <summary>
    /// Matches a top-level property name. A second occurrence of any routed property is rejected.
    /// </summary>
    /// <remarks>
    /// The gateway routes, authorises and bills on the value it parses, but forwards the body bytes
    /// as-is. Most upstream JSON parsers are last-key-wins, so a duplicate <c>model</c> (or
    /// <c>stream</c>/<c>max_tokens</c>) key would let the value the gateway checked differ from the
    /// value the upstream serves. Rather than pick a winner, the body is rejected as invalid JSON. A
    /// property counts as seen even when its value turns out to be the wrong kind, so a duplicate
    /// cannot resurrect a field the first occurrence disqualified.
    /// </remarks>
    private static TopLevelProperty ClassifyProperty(ref Utf8JsonReader reader, TopLevelScan scan)
    {
        if (reader.ValueTextEquals(ModelPropertyName))
        {
            ThrowIfDuplicate(scan.ModelSeen, "model");
            scan.ModelSeen = true;
            return TopLevelProperty.Model;
        }

        if (reader.ValueTextEquals(StreamPropertyName))
        {
            ThrowIfDuplicate(scan.StreamSeen, "stream");
            scan.StreamSeen = true;
            return TopLevelProperty.Stream;
        }

        if (reader.ValueTextEquals(MaxTokensPropertyName))
        {
            ThrowIfDuplicate(scan.MaxTokensSeen, "max_tokens");
            scan.MaxTokensSeen = true;
            return TopLevelProperty.MaxTokens;
        }

        if (reader.ValueTextEquals(MaxCompletionTokensPropertyName))
        {
            ThrowIfDuplicate(scan.MaxCompletionTokensSeen, "max_completion_tokens");
            scan.MaxCompletionTokensSeen = true;
            return TopLevelProperty.MaxCompletionTokens;
        }

        // Both are rewritten by the forwarder, which splices bytes at the offsets recorded here; a
        // second occurrence would be the one a last-key-wins upstream obeys.
        if (reader.ValueTextEquals(StreamOptionsPropertyName))
        {
            ThrowIfDuplicate(scan.StreamOptionsSeen, "stream_options");
            scan.StreamOptionsSeen = true;
            return TopLevelProperty.StreamOptions;
        }

        if (reader.ValueTextEquals(ServiceTierPropertyName))
        {
            ThrowIfDuplicate(scan.ServiceTierSeen, "service_tier");
            scan.ServiceTierSeen = true;
            return TopLevelProperty.ServiceTier;
        }

        return TopLevelProperty.None;
    }

    /// <summary>
    /// Walks the inside of a <c>stream_options</c> object: notes whether it holds any property and
    /// where <c>include_usage</c> is, and where the object closes.
    /// </summary>
    private static void ScanStreamOptions(ref Utf8JsonReader reader, TopLevelScan scan, long bufferStartOffset)
    {
        if (scan.IncludeUsagePending)
        {
            scan.IncludeUsagePending = false;
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                scan.StreamOptionsUnusable = true;
                return;
            }

            scan.IncludeUsage = reader.TokenType == JsonTokenType.True;
            scan.IncludeUsageValueRange = new JsonValueRange(bufferStartOffset + reader.TokenStartIndex, bufferStartOffset + reader.BytesConsumed);
            return;
        }

        if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 1)
        {
            scan.StreamOptionsEnd = bufferStartOffset + reader.BytesConsumed;
            scan.InStreamOptions = false;
            return;
        }

        if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 2)
        {
            return;
        }

        scan.StreamOptionsHasProperties = true;
        if (reader.ValueTextEquals(IncludeUsagePropertyName))
        {
            ThrowIfDuplicate(scan.IncludeUsageSeen, "stream_options.include_usage");
            scan.IncludeUsageSeen = true;
            scan.IncludeUsagePending = true;
        }
    }

    private static void ThrowIfDuplicate(bool alreadySeen, string propertyName)
    {
        if (alreadySeen)
        {
            throw new JsonException($"Duplicate top-level '{propertyName}' property in request body.");
        }
    }

    private static void ReadPendingValue(ref Utf8JsonReader reader, TopLevelScan scan, long bufferStartOffset)
    {
        switch (scan.Pending)
        {
            case TopLevelProperty.Model when reader.TokenType == JsonTokenType.String:
                scan.Model = reader.GetString();
                // TokenStartIndex is the opening quote and BytesConsumed is one past the closing
                // quote, so this range is exactly the raw token — escapes and all — and excludes any
                // surrounding whitespace or the following comma.
                scan.ModelValueRange = new JsonValueRange(bufferStartOffset + reader.TokenStartIndex, bufferStartOffset + reader.BytesConsumed);
                break;
            case TopLevelProperty.Stream when reader.TokenType is JsonTokenType.True or JsonTokenType.False:
                scan.Stream = reader.TokenType == JsonTokenType.True;
                break;
            case TopLevelProperty.MaxTokens when reader.TokenType == JsonTokenType.Number:
                if (reader.TryGetInt64(out var maxTokens) && maxTokens > 0)
                {
                    scan.MaxTokens = maxTokens;
                }

                break;
            case TopLevelProperty.MaxCompletionTokens when reader.TokenType == JsonTokenType.Number:
                if (reader.TryGetInt64(out var maxCompletionTokens) && maxCompletionTokens > 0)
                {
                    scan.MaxCompletionTokens = maxCompletionTokens;
                }

                break;
            case TopLevelProperty.StreamOptions:
                scan.StreamOptionsStart = bufferStartOffset + reader.TokenStartIndex;
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        // The end is recorded when the object closes.
                        scan.StreamOptionsIsObject = true;
                        scan.InStreamOptions = true;
                        break;
                    case JsonTokenType.StartArray:
                        scan.StreamOptionsUnusable = true;
                        scan.StreamOptionsEnd = scan.StreamOptionsStart;
                        break;
                    default:
                        scan.StreamOptionsEnd = bufferStartOffset + reader.BytesConsumed;
                        break;
                }

                break;
            case TopLevelProperty.ServiceTier when reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray):
                scan.ServiceTierValueRange = new JsonValueRange(bufferStartOffset + reader.TokenStartIndex, bufferStartOffset + reader.BytesConsumed);
                break;
        }
    }

    private enum TopLevelProperty
    {
        None,
        Model,
        Stream,
        MaxTokens,
        MaxCompletionTokens,
        StreamOptions,
        ServiceTier,
    }

    /// <summary>
    /// Scan state carried across buffer refills: a property name and its value can land in different
    /// reads, so which property is awaiting a value has to outlive a single reader.
    /// </summary>
    private sealed class TopLevelScan
    {
        public bool SawRootStart;
        public bool RootClosed;
        public TopLevelProperty Pending;
        public bool ModelSeen;
        public bool StreamSeen;
        public bool MaxTokensSeen;
        public bool MaxCompletionTokensSeen;
        public string? Model;
        public bool Stream;
        public long? MaxTokens;
        public long? MaxCompletionTokens;
        public JsonValueRange? ModelValueRange;
        public long RootStart;
        public bool StreamOptionsSeen;
        public bool InStreamOptions;
        public long StreamOptionsStart;
        public long StreamOptionsEnd;
        public bool StreamOptionsIsObject;
        public bool StreamOptionsHasProperties;
        public bool StreamOptionsUnusable;
        public bool IncludeUsageSeen;
        public bool IncludeUsagePending;
        public bool IncludeUsage;
        public JsonValueRange? IncludeUsageValueRange;
        public bool ServiceTierSeen;
        public JsonValueRange? ServiceTierValueRange;
    }
}
