namespace Autobahn.Domain.Models;
/// <summary>A model in a provider's catalogue, with the price the provider publishes for it.</summary>
public sealed class ProviderCatalogItem
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public int? ContextLength { get; init; }
    public decimal? InputPricePerMillionTokens { get; init; }
    public decimal? OutputPricePerMillionTokens { get; init; }
    public decimal? CachedInputPricePerMillionTokens { get; init; }
    public decimal? PricePerRequest { get; init; }
    /// <summary>Whether a model with this id is already registered.</summary>
    public bool Added { get; init; }
}
