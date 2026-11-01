using Autobahn.Domain.Configuration;

namespace Autobahn.Domain.RateLimiting;
/// <summary>Builds <see cref = "RateLimitScheduleReport"/> from the stored configuration.</summary>
public static class RateLimitScheduleReportBuilder
{
    public const int MaxTransitions = 500;
    /// <summary>
    /// Ceiling on the calendar spans one report may carry.
    /// </summary>
    /// <remarks>
    /// Transitions were capped from the start and occurrences were not, though they are built in the
    /// same loop and grow faster: a weekly window yields one occurrence per matching day, so the list
    /// scales as rules × windows × days. At the configured ceilings — 2,000 rules, 16 windows each,
    /// and the 62-day range the endpoint allows — that is nearly two million records serialised into
    /// one response, from a request an operator can repeat. Capped, with the total reported, so a
    /// console can say the calendar was trimmed rather than quietly drawing a partial one.
    /// </remarks>
    public const int MaxOccurrences = 2_000;
    public static RateLimitScheduleReport Build(IReadOnlyList<RateLimitRuleDefinition> rules, DateTimeOffset at, DateTimeOffset from, DateTimeOffset to, int take)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var statuses = new List<ScheduleRuleStatus>(rules.Count);
        var occurrences = new List<ScheduleOccurrence>();
        var transitions = new List<ScheduleTransition>();
        foreach (var rule in rules)
        {
            var basePolicy = rule.ToPolicy();
            var windows = rule.Windows;
            var evaluation = RateLimitScheduleEvaluator.Evaluate(basePolicy, windows, at);
            statuses.Add(new ScheduleRuleStatus(rule.Scope, rule.TargetKey, ScheduleTier.From(basePolicy), ScheduleTier.From(evaluation.Effective, evaluation.Suspended), evaluation.ActiveWindow?.Name, evaluation.ActiveUntil, evaluation.NextTransition, NextWindowName(windows, evaluation, at), windows.Select(w => DescribeWindow(w, at)).ToArray()));
            if (windows.Count == 0)
            {
                continue;
            }

            foreach (var window in windows)
            {
                foreach (var occurrence in RateLimitScheduleEvaluator.OccurrencesBetween(window, from, to))
                {
                    var start = occurrence.Start < from ? from : occurrence.Start;
                    var end = occurrence.End > to ? to : occurrence.End;
                    occurrences.Add(new ScheduleOccurrence(rule.Scope, rule.TargetKey, window.Name, start, end, ClippedStart: occurrence.Start < from, ClippedEnd: occurrence.End > to, ScheduleTier.From(window.ToPolicy(), window.Suspend)));
                }
            }

            transitions.AddRange(Transitions(rule, basePolicy, windows, from, to));
        }

        occurrences.Sort((a, b) => a.Start.CompareTo(b.Start));
        transitions.Sort((a, b) => a.At.CompareTo(b.At));
        var limit = Math.Clamp(take, 1, MaxTransitions);
        var truncated = transitions.Count > limit;
        // Both lists are trimmed from the front, so what survives is the soonest of each — the part a
        // calendar draws first and the part an operator is actually looking for.
        var occurrencesTruncated = occurrences.Count > MaxOccurrences;
        return new RateLimitScheduleReport(at, from, to, statuses, occurrencesTruncated ? occurrences.Take(MaxOccurrences).ToArray() : occurrences, truncated ? transitions.Take(limit).ToArray() : transitions, transitions.Count, truncated, occurrences.Count, occurrencesTruncated);
    }

    private static ScheduleWindowStatus DescribeWindow(RateLimitWindowDefinition window, DateTimeOffset at)
    {
        var error = RateLimitScheduleEvaluator.Describe(window);
        if (error is not null)
        {
            return new ScheduleWindowStatus(window.Name, window.Kind, "invalid", null, null, error);
        }

        if (RateLimitScheduleEvaluator.TryGetOccurrence(window, at, out var current))
        {
            var end = current.End == DateTimeOffset.MaxValue ? (DateTimeOffset? )null : current.End;
            return new ScheduleWindowStatus(window.Name, window.Kind, "active", current.Start, end, null);
        }

        var next = RateLimitScheduleEvaluator.NextOccurrence(window, at);
        if (next is { } n)
        {
            var end = n.End == DateTimeOffset.MaxValue ? (DateTimeOffset? )null : n.End;
            return new ScheduleWindowStatus(window.Name, window.Kind, "upcoming", n.Start, end, null);
        }

        return new ScheduleWindowStatus(window.Name, window.Kind, "expired", null, null, null);
    }

    /// <summary>The window that takes over at the next transition, when the next transition is a start.</summary>
    private static string? NextWindowName(IReadOnlyList<RateLimitWindowDefinition> windows, ScheduleEvaluation evaluation, DateTimeOffset at)
    {
        if (evaluation.NextTransition is not { } next)
        {
            return null;
        }

        var after = RateLimitScheduleEvaluator.Evaluate(RateLimitPolicy.Unlimited, windows, next);
        _ = at;
        return after.ActiveWindow?.Name;
    }

    /// <summary>
    /// The moments in <c>[from, to)</c> at which the rule's effective tier changes, found by
    /// evaluating the rule at every occurrence boundary and keeping the ones where the answer moved.
    /// </summary>
    private static IEnumerable<ScheduleTransition> Transitions(RateLimitRuleDefinition rule, RateLimitPolicy basePolicy, IReadOnlyList<RateLimitWindowDefinition> windows, DateTimeOffset from, DateTimeOffset to)
    {
        var boundaries = new SortedSet<DateTimeOffset>();
        foreach (var window in windows)
        {
            foreach (var occurrence in RateLimitScheduleEvaluator.OccurrencesBetween(window, from, to))
            {
                if (occurrence.Start > from && occurrence.Start < to)
                {
                    boundaries.Add(occurrence.Start);
                }

                if (occurrence.End > from && occurrence.End < to)
                {
                    boundaries.Add(occurrence.End);
                }
            }
        }

        var previous = RateLimitScheduleEvaluator.Evaluate(basePolicy, windows, from);
        foreach (var boundary in boundaries)
        {
            var current = RateLimitScheduleEvaluator.Evaluate(basePolicy, windows, boundary);
            if (current.Effective != previous.Effective || current.Suspended != previous.Suspended || current.ActiveWindow?.Name != previous.ActiveWindow?.Name)
            {
                yield return new ScheduleTransition(boundary, rule.Scope, rule.TargetKey, current.ActiveWindow?.Name, ScheduleTier.From(previous.Effective, previous.Suspended), ScheduleTier.From(current.Effective, current.Suspended));
            }

            previous = current;
        }
    }
}
