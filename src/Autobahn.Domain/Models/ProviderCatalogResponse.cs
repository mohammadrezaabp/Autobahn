namespace Autobahn.Domain.Models;
public sealed class ProviderCatalogResponse
{
    public required string Provider { get; init; }
    public int Total { get; init; }
    public required IReadOnlyList<ProviderCatalogItem> Models { get; init; }
}
