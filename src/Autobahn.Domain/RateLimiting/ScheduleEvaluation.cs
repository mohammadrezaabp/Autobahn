using System.Globalization;

namespace Autobahn.Domain.RateLimiting;
/// <summary>What a rule enforces at one instant, and when that next changes.</summary>
/// <param name = "Effective">The tier in force: the winning window's, or the base tier.</param>
/// <param name = "ActiveWindow">The window whose tier is in force, or null for the base tier.</param>
/// <param name = "ActiveUntil">When the active window's current occurrence ends; null for the base tier or an open-ended window.</param>
/// <param name = "NextTransition">
/// The earliest instant at which the answer might differ, or null when nothing is scheduled ahead.
/// Conservative: it may name an instant at which nothing visible changes, never one later than a
/// real change.
/// </param>
/// <param name = "Suspended">Whether the active window suspends the rule outright.</param>
public sealed record ScheduleEvaluation(RateLimitPolicy Effective, RateLimitWindowDefinition? ActiveWindow, DateTimeOffset? ActiveUntil, DateTimeOffset? NextTransition, bool Suspended)
{
    public bool IsBase => ActiveWindow is null;
}
