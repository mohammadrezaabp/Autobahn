namespace Autobahn.Domain.Billing;

public sealed record RateCardRecord(
    Guid Id,
    string Slug,
    string Name,
    string ModelId,
    decimal InputPricePerMillionTokens,
    decimal OutputPricePerMillionTokens,
    string Currency,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// The price of prompt tokens the upstream served from its cache. Null prices them as ordinary
    /// input, which is what every card did before the field existed.
    /// </summary>
    public decimal? CachedInputPricePerMillionTokens { get; init; }

    /// <summary>A flat charge per request, for models priced per call (rerank). 0 for none.</summary>
    public decimal PricePerRequest { get; init; }
}
