using System.Buffers;
using System.Text.Json;

namespace Autobahn.Proxy.Parsing;
/// <summary>
/// Byte range of a value inside the request body, relative to the stream position parsing started
/// from.
/// </summary>
/// <remarks>
/// Only meaningful for a body the caller can seek back to that same position. Every gateway caller
/// parses from position 0 of the buffered request body, so the offsets are absolute in practice.
/// </remarks>
public readonly record struct JsonValueRange(long Start, long End)
{
    public long Length => End - Start;
}
