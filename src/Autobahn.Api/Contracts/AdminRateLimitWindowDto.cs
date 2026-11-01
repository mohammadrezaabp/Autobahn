using Autobahn.Domain.Configuration;
using Autobahn.Domain.RateLimiting;

namespace Autobahn.Api.Contracts;
/// <summary>
/// One schedule window on a rule. <c>once</c> windows use <paramref name = "From"/> and
/// <paramref name = "Until"/>; <c>weekly</c> windows use <paramref name = "Days"/>,
/// <paramref name = "Start"/>, <paramref name = "End"/> and <paramref name = "TimeZone"/>.
/// </summary>
public sealed record AdminRateLimitWindowDto(string Name, string Kind, int Rpm, int Burst, int MaxConcurrentStreams, bool Suspend = false, int? Priority = null, DateTimeOffset? From = null, DateTimeOffset? Until = null, List<string>? Days = null, string? Start = null, string? End = null, string? TimeZone = null, DateTimeOffset? ValidFrom = null, DateTimeOffset? ValidUntil = null)
{
    /// <summary>Tokens per minute while the window is active; zero means no token limit for its span.</summary>
    public int Tpm { get; init; }

    public RateLimitWindowDefinition ToDefinition() => new(Name?.Trim() ?? string.Empty, Kind?.Trim().ToLowerInvariant() ?? string.Empty, Rpm, Burst, MaxConcurrentStreams, Suspend, Priority, From, Until, Days?.Select(static d => d?.Trim().ToLowerInvariant() ?? string.Empty).ToArray(), Start?.Trim(), End?.Trim(), string.IsNullOrWhiteSpace(TimeZone) ? null : TimeZone.Trim(), ValidFrom, ValidUntil)
    {
        Tpm = Tpm,
    };
    public static AdminRateLimitWindowDto FromDefinition(RateLimitWindowDefinition w) => new(w.Name, w.Kind, w.Rpm, w.Burst, w.MaxConcurrentStreams, w.Suspend, w.Priority, w.From, w.Until, w.Days?.ToList(), w.Start, w.End, w.TimeZone, w.ValidFrom, w.ValidUntil)
    {
        Tpm = w.Tpm,
    };
}
