using Autobahn.Domain.Configuration;

namespace Autobahn.Domain.RateLimiting;
/// <summary>One span on the calendar: a window's occurrence, clipped to the requested range.</summary>
public sealed record ScheduleOccurrence(string Scope, string Target, string Window, DateTimeOffset Start, DateTimeOffset End, bool ClippedStart, bool ClippedEnd, ScheduleTier Tier);
