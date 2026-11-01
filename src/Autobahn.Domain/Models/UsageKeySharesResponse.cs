namespace Autobahn.Domain.Models;
/// <summary>
/// How much of the filtered load each API key carried: one row per key (plus one for requests sent
/// without a key), with its share of requests, tokens and cost in the same filter.
/// </summary>
/// <remarks>
/// Aggregated from the billing ledger: the daily rollup table has no per-key dimension. Typically
/// read with a model filter to answer "who is driving this model's traffic".
/// </remarks>
public sealed class UsageKeySharesResponse
{
    /// <summary>The model filter the shares were computed over; <see langword="null"/> is every model.</summary>
    public string? ModelId { get; init; }
    public string Currency { get; init; } = "USD";
    public int TotalRequests { get; init; }
    public long TotalTokens { get; init; }
    public decimal TotalCost { get; init; }
    /// <summary>Largest share of requests first.</summary>
    public required IReadOnlyList<UsageKeyShare> Keys { get; init; }
}
