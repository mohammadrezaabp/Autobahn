namespace Autobahn.Domain.Models;

/// <param name="BackendName">Which of the model's backends this verdict is about.</param>
/// <param name="LastTransitionUtc">When <paramref name="IsHealthy"/> last flipped; null until it has.</param>
public sealed record BackendHealth(
    string ModelId,
    string Url,
    bool IsHealthy,
    int? StatusCode,
    string? Error,
    DateTimeOffset LastCheckedUtc,
    DateTimeOffset? LastTransitionUtc = null,
    string BackendName = ModelBackends.DefaultName)
{
    /// <summary>The key this verdict is filed under (see <see cref="ModelBackends.Key"/>).</summary>
    public string Key => ModelBackends.Key(ModelId, BackendName);
}
