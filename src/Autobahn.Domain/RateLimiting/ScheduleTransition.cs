using Autobahn.Domain.Configuration;

namespace Autobahn.Domain.RateLimiting;
/// <summary>One moment at which a rule's effective tier changes.</summary>
public sealed record ScheduleTransition(DateTimeOffset At, string Scope, string Target, string? Window, ScheduleTier From, ScheduleTier To);
