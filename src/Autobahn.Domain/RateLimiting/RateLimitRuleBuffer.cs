using System.Runtime.CompilerServices;

namespace Autobahn.Domain.RateLimiting;
/// <summary>
/// A stack buffer for the rules that apply to one request. Six is the ceiling by construction:
/// global, tenant, key, model, tenant×model, key×model — one per <see cref = "RateLimitScope"/> that
/// a request can be subject to.
/// </summary>
/// <remarks>
/// An inline array rather than a rented or allocated one because this sits on the hot path of every
/// inference request. The rules are built into it, handed to the store as a
/// <see cref = "System.Span{T}"/>, and go out of scope with the frame — no allocation, no pool to
/// return to, and no way to leak one across requests.
/// </remarks>
[InlineArray(MaxRules)]
public struct RateLimitRuleBuffer
{
    public const int MaxRules = 6;
#pragma warning disable IDE0051, CS0169 // The inline-array element; the runtime projects it into MaxRules slots.

    private RateLimitRule _element0;
#pragma warning restore IDE0051, CS0169
}
