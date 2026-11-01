using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Autobahn.Proxy.Parsing;

namespace Autobahn.Proxy.Forwarding;
/// <summary>
/// Forwards the buffered request body with a few byte ranges replaced — the client's <c>model</c>
/// value by the canonical id, a <c>stream_options</c> that asks for usage — streaming straight from
/// the buffer instead of materialising a rewritten copy.
/// </summary>
/// <remarks>
/// <para>The rewrite it replaces read the body into a <see cref = "string "/>, re-parsed it into a
/// <see cref = "JsonDocument"/>, wrote it back through a growing <c>MemoryStream</c>, copied that with
/// <c>ToArray()</c>, decoded the result to another string and finally re-encoded it into a
/// <c>StringContent</c>. Measured at ~13x the body size in allocations, essentially all of it on the
/// Large Object Heap, per aliased request — which at the default 25 MB body cap and 64 concurrent
/// forwards per model is several times the memory a gateway pod is given.</para>
///
/// <para>Only the spliced tokens move, so everything else — key order, whitespace, escape
/// sequences, non-ASCII content — reaches the upstream byte for byte. The previous rewrite
/// round-tripped the body through UTF-16, which silently replaced malformed UTF-8 with U+FFFD.</para>
///
/// <para>Serialisation is repeatable: the body is seeked from the start each time, so a request that
/// <see cref = "HttpClient"/> retries on a fresh connection still sends a complete payload.</para>
/// </remarks>
internal sealed class SplicedRequestContent : HttpContent
{
    private const int CopyBufferBytes = 64 * 1024;
    private readonly Stream _body;
    private readonly RequestBodySplice[] _splices;
    private SplicedRequestContent(Stream body, RequestBodySplice[] splices)
    {
        _body = body;
        _splices = splices;
    }

    /// <summary>
    /// Builds the rewritten content, or returns null when a splice does not describe a range inside
    /// <paramref name = "body"/> or two of them overlap — in which case the caller must forward the
    /// body unchanged rather than send something truncated.
    /// </summary>
    public static SplicedRequestContent? TryCreate(Stream body, IEnumerable<RequestBodySplice> splices, MediaTypeHeaderValue? contentType)
    {
        var ordered = splices.OrderBy(s => s.Range.Start).ThenBy(s => s.Range.End).ToArray();
        if (!body.CanSeek || ordered.Length == 0)
        {
            return null;
        }

        var previousEnd = 0L;
        foreach (var splice in ordered)
        {
            if (splice.Range.Start < previousEnd || splice.Range.End < splice.Range.Start || splice.Range.End > body.Length)
            {
                return null;
            }

            previousEnd = splice.Range.End;
        }

        var content = new SplicedRequestContent(body, ordered);
        content.Headers.ContentType = contentType ?? new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8",
        };
        return content;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        _body.Position = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        try
        {
            foreach (var splice in _splices)
            {
                await CopyExactlyAsync(_body, stream, splice.Range.Start - _body.Position, buffer, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(splice.Replacement, cancellationToken).ConfigureAwait(false);
                _body.Position = splice.Range.End;
            }

            await CopyExactlyAsync(_body, stream, _body.Length - _body.Position, buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _body.Length;
        foreach (var splice in _splices)
        {
            length += splice.Replacement.Length - splice.Range.Length;
        }

        return true;
    }

    private static async Task CopyExactlyAsync(Stream source, Stream destination, long count, byte[] buffer, CancellationToken cancellationToken)
    {
        var remaining = count;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("Buffered request body ended before the expected number of bytes.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }
}
