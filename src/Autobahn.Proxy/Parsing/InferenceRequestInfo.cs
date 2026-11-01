using System.Buffers;
using System.Text.Json;

namespace Autobahn.Proxy.Parsing;
/// <param name = "ModelValueRange">
/// Where the raw <c>model</c> value token sits in the body — quotes included, surrounding whitespace
/// and delimiters excluded. Lets the forwarder swap an alias for the canonical id by splicing bytes
/// instead of re-serialising the document.
/// </param>
public readonly record struct InferenceRequestInfo(string? Model, bool Stream, long? MaxTokens = null, JsonValueRange? ModelValueRange = null)
{
    /// <summary>Offset of the root object's opening brace: where a property can be inserted.</summary>
    public long RootStart { get; init; }
    /// <summary>Where the client's <c>stream_options</c> sits; null when the body has none.</summary>
    public StreamOptionsLayout? StreamOptions { get; init; }
    /// <summary>Where the raw <c>service_tier</c> value token sits; null when the body has none.</summary>
    public JsonValueRange? ServiceTierValueRange { get; init; }
    /// <summary>Whether the client asked for the terminal usage frame of a stream itself.</summary>
    public bool ClientRequestedStreamUsage => StreamOptions is { IncludeUsage: true };
}
