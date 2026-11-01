namespace Autobahn.Domain.Models;
public sealed class ConfigStatusModel
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
}
