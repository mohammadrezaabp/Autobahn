using System.Globalization;

namespace Autobahn.Domain.RateLimiting;
/// <summary>One span during which a window is active: <c>[Start, End)</c> in UTC.</summary>
public readonly record struct WindowOccurrence(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Contains(DateTimeOffset instant) => Start <= instant && instant < End;
}
