using System.Buffers;

namespace Autobahn.Proxy.Forwarding;
/// <summary>
/// Receives the retained window of a proxied response body once the stream completes.
/// </summary>
/// <param name = "head">
/// Leading bytes, retained only for non-streaming responses and only up to
/// <see cref = "UsageCapturingStream.MaxHeadBytes"/>. Empty for streaming responses.
/// </param>
/// <param name = "tail">Trailing bytes, always retained, up to <see cref = "UsageCapturingStream.TailBufferBytes"/>.</param>
/// <param name = "stats">What was observed of the whole body, independent of the retained window.</param>
internal delegate void UsageCaptureCallback(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail, UsageCaptureStats stats);
