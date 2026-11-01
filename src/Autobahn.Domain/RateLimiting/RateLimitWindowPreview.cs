using Autobahn.Domain.Configuration;

namespace Autobahn.Domain.RateLimiting;
/// <summary>
/// What an operator sees while composing a window, before committing it: whether it is valid, when
/// it next runs, and how it stands against the rule's other windows.
/// </summary>
public sealed record RateLimitWindowPreview(bool Valid, string? Error, DateTimeOffset? NextStartAt, DateTimeOffset? NextEndAt, bool ActiveNow, IReadOnlyList<string> Overlaps, IReadOnlyList<string> OutrankedBy, IReadOnlyList<string> Outranks);
