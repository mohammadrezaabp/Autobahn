using Autobahn.Domain.Configuration;

namespace Autobahn.Domain.RateLimiting;
/// <summary>One rule's standing at the reference instant.</summary>
public sealed record ScheduleRuleStatus(string Scope, string Target, ScheduleTier Base, ScheduleTier Effective, string? ActiveWindow, DateTimeOffset? ActiveUntil, DateTimeOffset? NextChangeAt, string? NextWindow, IReadOnlyList<ScheduleWindowStatus> Windows);
