using System.Text.Json;

namespace Autobahn.Proxy.Forwarding;

/// <summary>
/// Passes an SSE body through event by event, leaving out the usage-only chunk an OpenAI-compatible
/// upstream sends when asked with <c>stream_options.include_usage</c>.
/// </summary>
/// <remarks>
/// <para>The gateway asks for that chunk on the client's behalf, because without it a stream is
/// billed from an estimate (see <see cref="StreamingHttpTransformer.RequestStreamUsage"/>). A client
/// that did not ask for it does not expect it: its <c>choices</c> array is empty, and code that reads
/// <c>choices[0]</c> of every chunk fails on it. The usage capture sits beneath this filter, so it
/// still reads the chunk.</para>
///
/// <para>Events are released whole as soon as their blank-line terminator arrives, so streaming
/// latency is unchanged. A run of bytes with no terminator in sight is released as it is rather
/// than held without bound.</para>
/// </remarks>
internal sealed class SseUsageChunkFilterStream(Stream inner) : Stream
{
    private const int ReadChunkBytes = 16 * 1024;

    /// <summary>A pending event larger than this is not an SSE event the filter can judge; it is released unfiltered.</summary>
    internal const int MaxPendingBytes = 1024 * 1024;

    private byte[] _pending = new byte[ReadChunkBytes];
    private int _pendingLength;
    private byte[] _ready = [];
    private int _readyOffset;
    private int _readyLength;
    private bool _innerDone;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        while (true)
        {
            if (TryTakeReady(buffer, out var taken))
            {
                return taken;
            }

            if (_innerDone)
            {
                return 0;
            }

            EnsurePendingSpace();
            var read = inner.Read(_pending.AsSpan(_pendingLength));
            Accept(read);
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (TryTakeReady(buffer.Span, out var taken))
            {
                return taken;
            }

            if (_innerDone)
            {
                return 0;
            }

            EnsurePendingSpace();
            var read = await inner.ReadAsync(_pending.AsMemory(_pendingLength), cancellationToken).ConfigureAwait(false);
            Accept(read);
        }
    }

    private bool TryTakeReady(Span<byte> destination, out int taken)
    {
        taken = 0;
        if (_readyLength == 0 || destination.IsEmpty)
        {
            return false;
        }

        taken = Math.Min(destination.Length, _readyLength);
        _ready.AsSpan(_readyOffset, taken).CopyTo(destination);
        _readyOffset += taken;
        _readyLength -= taken;
        return true;
    }

    private void EnsurePendingSpace()
    {
        if (_pending.Length - _pendingLength < ReadChunkBytes)
        {
            Array.Resize(ref _pending, _pending.Length * 2);
        }
    }

    /// <summary>Takes newly read bytes and moves every complete event that is not usage-only to the ready buffer.</summary>
    private void Accept(int read)
    {
        if (read == 0)
        {
            // Whatever is left was never terminated; it goes through as it came.
            _innerDone = true;
            Release(_pending.AsSpan(0, _pendingLength));
            _pendingLength = 0;
            return;
        }

        _pendingLength += read;
        var output = new List<byte[]>();
        var consumed = 0;
        while (FindEventEnd(_pending.AsSpan(consumed, _pendingLength - consumed)) is var end and > 0)
        {
            var sseEvent = _pending.AsSpan(consumed, end);
            if (!IsUsageOnlyChunk(sseEvent))
            {
                output.Add(sseEvent.ToArray());
            }

            consumed += end;
        }

        if (_pendingLength - consumed > MaxPendingBytes)
        {
            output.Add(_pending.AsSpan(consumed, _pendingLength - consumed).ToArray());
            consumed = _pendingLength;
        }

        _pending.AsSpan(consumed, _pendingLength - consumed).CopyTo(_pending);
        _pendingLength -= consumed;

        foreach (var chunk in output)
        {
            Release(chunk);
        }
    }

    private void Release(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        var unread = _ready.AsSpan(_readyOffset, _readyLength);
        var combined = new byte[unread.Length + bytes.Length];
        unread.CopyTo(combined);
        bytes.CopyTo(combined.AsSpan(unread.Length));
        _ready = combined;
        _readyOffset = 0;
        _readyLength = combined.Length;
    }

    /// <summary>Length of the first complete event, its blank-line terminator included; 0 when none is complete.</summary>
    internal static int FindEventEnd(ReadOnlySpan<byte> data)
    {
        var lf = data.IndexOf("\n\n"u8);
        var crlf = data.IndexOf("\r\n\r\n"u8);
        if (lf < 0 && crlf < 0)
        {
            return 0;
        }

        if (crlf >= 0 && (lf < 0 || crlf < lf))
        {
            return crlf + 4;
        }

        return lf + 2;
    }

    /// <summary>
    /// Whether <paramref name="sseEvent"/> is the chunk <c>include_usage</c> adds: a <c>data:</c>
    /// payload whose <c>choices</c> is empty and whose <c>usage</c> is an object.
    /// </summary>
    internal static bool IsUsageOnlyChunk(ReadOnlySpan<byte> sseEvent)
    {
        if (sseEvent.IndexOf("\"usage\""u8) < 0 || sseEvent.IndexOf("\"choices\""u8) < 0)
        {
            return false;
        }

        var payload = new List<byte>();
        var remaining = sseEvent;
        while (!remaining.IsEmpty)
        {
            var newline = remaining.IndexOf((byte)'\n');
            var line = newline < 0 ? remaining : remaining[..newline];
            remaining = newline < 0 ? [] : remaining[(newline + 1)..];
            line = line.TrimEnd((byte)'\r');
            if (!line.StartsWith("data:"u8))
            {
                continue;
            }

            if (payload.Count > 0)
            {
                payload.Add((byte)'\n');
            }

            payload.AddRange(line[5..].TrimStart((byte)' '));
        }

        if (payload.Count == 0)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload.ToArray());
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("choices", out var choices) &&
                   choices.ValueKind == JsonValueKind.Array &&
                   choices.GetArrayLength() == 0 &&
                   root.TryGetProperty("usage", out var usage) &&
                   usage.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
