using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Autobahn.Proxy.Parsing;

namespace Autobahn.Proxy.Forwarding;
/// <summary>One change to the buffered request body: <see cref = "Range"/> is replaced by <see cref = "Replacement"/>.</summary>
/// <remarks>An empty range inserts at its start.</remarks>
internal readonly record struct RequestBodySplice(JsonValueRange Range, byte[] Replacement)
{
    /// <summary>Replaces a value token with <paramref name = "value"/> as a JSON string, quotes included.</summary>
    public static RequestBodySplice JsonString(JsonValueRange range, string value)
    {
        var encoded = JsonEncodedText.Encode(value).EncodedUtf8Bytes;
        var buffer = new byte[encoded.Length + 2];
        buffer[0] = (byte)'"';
        encoded.CopyTo(buffer.AsSpan(1));
        buffer[^1] = (byte)'"';
        return new RequestBodySplice(range, buffer);
    }

    public static RequestBodySplice Insert(long at, ReadOnlySpan<byte> bytes) => new(new JsonValueRange(at, at), bytes.ToArray());
}
