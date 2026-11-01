namespace Autobahn.Domain.Models;
public sealed class AddProviderModelsResponse
{
    public required IReadOnlyList<AddProviderModelResult> Results { get; init; }
}
