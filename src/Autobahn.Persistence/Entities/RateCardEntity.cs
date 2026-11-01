namespace Autobahn.Persistence.Entities;

public sealed class RateCardEntity
{
    public Guid Id { get; set; }

    public required string Slug { get; set; }

    public required string Name { get; set; }

    public required string ModelId { get; set; }

    public decimal InputPricePerMillionTokens { get; set; }

    public decimal OutputPricePerMillionTokens { get; set; }

    public decimal? CachedInputPricePerMillionTokens { get; set; }

    public decimal PricePerRequest { get; set; }

    public required string Currency { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveUntil { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
