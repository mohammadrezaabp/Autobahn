namespace Autobahn.Domain.Models;
public sealed class AddProviderModelsRequest
{
    public IReadOnlyList<string> Models { get; init; } = [];
}
