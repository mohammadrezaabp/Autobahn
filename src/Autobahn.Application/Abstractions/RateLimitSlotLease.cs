using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;
/// <summary>The concurrency slots one request holds, so exactly those can be given back.</summary>
/// <remarks>
/// A fixed-size buffer rather than a list: the rule set is capped at
/// <see cref = "RateLimitRuleBuffer.MaxRules"/>, so the lease never needs to allocate, and a streaming
/// response holds one of these for its whole lifetime.
/// </remarks>
public readonly struct RateLimitSlotLease
{
    private readonly string[]? _keys;
    public RateLimitSlotLease(string[]? keys, int count)
    {
        _keys = keys;
        Count = count;
    }

    public int Count { get; }
    public ReadOnlySpan<string> Keys => _keys is null ? ReadOnlySpan<string>.Empty : _keys.AsSpan(0, Count);
    public static RateLimitSlotLease Empty => default;
}
