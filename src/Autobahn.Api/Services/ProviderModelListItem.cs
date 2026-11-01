using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Autobahn.Api.Services;
public sealed record ProviderModelListItem(string Id, string? Name, int? ContextLength)
{
    public decimal? InputPricePerMillionTokens { get; init; }
    public decimal? OutputPricePerMillionTokens { get; init; }
    public decimal? CachedInputPricePerMillionTokens { get; init; }
    /// <summary>A flat charge per call, for models priced that way.</summary>
    public decimal? PricePerRequest { get; init; }
}
