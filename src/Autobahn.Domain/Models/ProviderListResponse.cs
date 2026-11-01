namespace Autobahn.Domain.Models;
public sealed class ProviderListResponse
{
    public required IReadOnlyList<ProviderOverviewItem> Providers { get; init; }
}
