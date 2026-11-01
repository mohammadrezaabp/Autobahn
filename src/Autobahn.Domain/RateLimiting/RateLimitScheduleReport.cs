using Autobahn.Domain.Configuration;

namespace Autobahn.Domain.RateLimiting;
/// <summary>
/// What every rule enforces at one instant, what it will enforce over a range, and when each
/// change happens. Served from <c>GET /admin/api/rate-limits/schedule</c>.
/// </summary>
public sealed record RateLimitScheduleReport(DateTimeOffset At, DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ScheduleRuleStatus> Rules, IReadOnlyList<ScheduleOccurrence> Occurrences, IReadOnlyList<ScheduleTransition> Transitions, int TransitionsTotal, bool TransitionsTruncated, int OccurrencesTotal = 0, bool OccurrencesTruncated = false);
