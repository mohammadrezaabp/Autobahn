using Autobahn.Domain.Configuration;

namespace Autobahn.Domain.RateLimiting;
/// <summary>One window as the report describes it: its state at the reference instant, and why if it cannot run.</summary>
/// <param name = "State">One of <c>active</c>, <c>upcoming</c>, <c>expired</c>, <c>invalid</c>.</param>
public sealed record ScheduleWindowStatus(string Name, string Kind, string State, DateTimeOffset? NextStartAt, DateTimeOffset? NextEndAt, string? Error);
