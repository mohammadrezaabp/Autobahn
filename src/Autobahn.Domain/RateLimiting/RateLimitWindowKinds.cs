namespace Autobahn.Domain.RateLimiting;
/// <summary>The two shapes a schedule window can take, as stored and transported.</summary>
public static class RateLimitWindowKinds
{
    /// <summary>A single span: from an instant, optionally until another. No <c>Until</c> is an open-ended step change.</summary>
    public const string Once = "once";
    /// <summary>A recurring span: on the listed days of the week, from a local start time to a local end time, in a time zone.</summary>
    public const string Weekly = "weekly";
    public static bool IsKnown(string? kind) => string.Equals(kind, Once, StringComparison.OrdinalIgnoreCase) || string.Equals(kind, Weekly, StringComparison.OrdinalIgnoreCase);
}
