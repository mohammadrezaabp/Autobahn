using System.Buffers;
using System.Text.Json;

namespace Autobahn.Proxy.Parsing;
/// <summary>The client's <c>stream_options</c>, as far as asking for a stream's usage needs it.</summary>
/// <param name = "Value">The whole value token, or the whole object, as raw bytes.</param>
/// <param name = "IsObject">False for <c>null</c> or any other scalar.</param>
/// <param name = "HasProperties">The object holds at least one property.</param>
/// <param name = "IncludeUsage"><c>include_usage</c> is <c>true</c>.</param>
/// <param name = "IncludeUsageValueRange">Where the raw <c>include_usage</c> value sits, when it is a scalar.</param>
/// <param name = "Unusable">A shape no upstream accepts (an array, a nested <c>include_usage</c>): left as it is.</param>
public readonly record struct StreamOptionsLayout(JsonValueRange Value, bool IsObject, bool HasProperties, bool IncludeUsage, JsonValueRange? IncludeUsageValueRange, bool Unusable);
