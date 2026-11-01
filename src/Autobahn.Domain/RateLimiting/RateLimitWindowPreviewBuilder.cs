using Autobahn.Domain.Configuration;

namespace Autobahn.Domain.RateLimiting;
public static class RateLimitWindowPreviewBuilder
{
    /// <param name = "rule">The rule with every window it would have, the candidate included.</param>
    /// <param name = "candidateName">Which of those windows is being composed.</param>
    public static RateLimitWindowPreview Build(RateLimitRuleDefinition rule, string candidateName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var windows = rule.Windows;
        // The size gate comes before anything is computed. Every other refusal below still describes
        // the rule in full — a composer needs to be told *why* its window is invalid and what it
        // clashes with — but the overlap scan is quadratic in the window count, and the window
        // ceiling is itself one of the rules validation enforces. So a set past that ceiling is the
        // one case where continuing would mean running a scan whose size the caller chose, and it is
        // answered here with the refusal a save would give it and no work at all.
        if (windows.Count > RateLimitConfigValidation.MaxWindowsPerRule)
        {
            return new RateLimitWindowPreview(false, $"A rule may not have more than {RateLimitConfigValidation.MaxWindowsPerRule} windows.", null, null, false, [], [], []);
        }

        var candidate = windows.FirstOrDefault(w => string.Equals(w.Name, candidateName, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
        {
            return new RateLimitWindowPreview(false, "The window to preview is not in the rule.", null, null, false, [], [], []);
        }

        string? error = null;
        if (!RateLimitConfigValidation.TryValidateRules([rule], out var validationError))
        {
            error = validationError;
        }

        var overlaps = OverlapsWith(windows, candidateName);
        var others = windows.Where(w => !ReferenceEquals(w, candidate)).ToArray();
        var outrankedBy = others.Where(w => w.Rank > candidate.Rank).Select(w => w.Name).ToArray();
        var outranks = others.Where(w => w.Rank < candidate.Rank).Select(w => w.Name).ToArray();
        var activeNow = RateLimitScheduleEvaluator.TryGetOccurrence(candidate, now, out var current);
        WindowOccurrence? next = activeNow ? current : RateLimitScheduleEvaluator.NextOccurrence(candidate, now);
        return new RateLimitWindowPreview(error is null, error, next?.Start, next is { } n && n.End != DateTimeOffset.MaxValue ? n.End : null, activeNow, overlaps, outrankedBy, outranks);
    }

    /// <summary>The other windows the named one can be active alongside without a clear winner.</summary>
    private static string[] OverlapsWith(IReadOnlyList<RateLimitWindowDefinition> windows, string candidateName) => [..RateLimitConfigValidation.FindWindowOverlaps(windows).Where(pair => Involves(pair, candidateName)).Select(pair => string.Equals(pair.First, candidateName, StringComparison.OrdinalIgnoreCase) ? pair.Second : pair.First).Distinct(StringComparer.OrdinalIgnoreCase)];
    private static bool Involves((string First, string Second) pair, string name) => string.Equals(pair.First, name, StringComparison.OrdinalIgnoreCase) || string.Equals(pair.Second, name, StringComparison.OrdinalIgnoreCase);
}
