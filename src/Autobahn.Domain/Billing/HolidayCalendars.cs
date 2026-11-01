namespace Autobahn.Domain.Billing;
/// <summary>The holiday calendars a peak-hours schedule can name.</summary>
public static class HolidayCalendars
{
    /// <summary>
    /// Mainland China's public holidays as the State Council arranges them, which DeepSeek's
    /// off-peak days follow. The arrangement is announced late each year for the next.
    /// </summary>
    public const string China = "china";
    public static readonly IReadOnlyList<string> Known = [China];
    public static bool IsKnown(string? name) => Known.Contains(name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}
