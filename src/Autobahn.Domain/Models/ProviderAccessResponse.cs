namespace Autobahn.Domain.Models;
public sealed class ProviderAccessResponse
{
    public required IReadOnlyList<ProviderAccessItem> Providers { get; init; }
}
