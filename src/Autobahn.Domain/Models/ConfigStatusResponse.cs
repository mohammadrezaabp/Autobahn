namespace Autobahn.Domain.Models;
public sealed class ConfigStatusResponse
{
    public bool HotReloadEnabled { get; init; }
    public bool WatchEnabled { get; init; }
    public DateTimeOffset? LastReload { get; init; }
    public int ModelCount { get; init; }
    public IReadOnlyList<ConfigStatusModel> Models { get; init; } = [];
}
