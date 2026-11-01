namespace Autobahn.Domain.Models.Overview;
/// <summary>What the schedule says now, from the same computation as the rate-limits calendar.</summary>
/// <param name = "Available">False when the schedule could not be read; the counts are then unknown, not zero.</param>
/// <param name = "ScheduledRuleCount">Enabled rules that carry at least one window.</param>
/// <param name = "WindowsActiveNow">Rules whose tier is currently set by a window.</param>
/// <param name = "NextChangeAtUtc">The soonest future moment any rule's tier changes; null when none is scheduled.</param>
/// <param name = "NextChangeRuleId">That rule's <c>scope:target</c> identity, lower case.</param>
/// <param name = "NextChangeWindow">The window that starts then, when the change is a start.</param>
public sealed record RateLimitScheduleOverview(bool Available, int ScheduledRuleCount, int WindowsActiveNow, DateTimeOffset? NextChangeAtUtc, string? NextChangeRuleId, string? NextChangeWindow)
{
    public static RateLimitScheduleOverview Unavailable { get; } = new(false, 0, 0, null, null, null);
}
