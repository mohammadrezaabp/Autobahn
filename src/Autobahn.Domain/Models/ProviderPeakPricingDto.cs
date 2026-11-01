namespace Autobahn.Domain.Models;
/// <summary>A provider's peak hours, from <c>Billing:PeakPricing</c>.</summary>
public sealed class ProviderPeakPricingDto
{
    /// <summary>Peak price over the rate card's price, which is the off-peak one.</summary>
    public decimal Multiplier { get; init; }
    public DateTimeOffset? EffectiveFromUtc { get; init; }
    /// <summary>English day names, Monday first.</summary>
    public required IReadOnlyList<string> Days { get; init; }
    /// <summary><c>HH:mm-HH:mm</c> in UTC, the end excluded.</summary>
    public required IReadOnlyList<string> WindowsUtc { get; init; }
    /// <summary>The holiday calendar whose days off have no peak hours; null for none.</summary>
    public string? HolidayCalendar { get; init; }
    /// <summary>Days with no peak hours from today to the end of next year: the calendar's and the configured ones.</summary>
    public required IReadOnlyList<DateOnly> UpcomingExcludedDates { get; init; }
    /// <summary>
    /// This year and the next, where the calendar has no holidays for them yet, so every weekday
    /// is priced with its peak hours.
    /// </summary>
    public required IReadOnlyList<int> YearsWithoutHolidays { get; init; }
}
