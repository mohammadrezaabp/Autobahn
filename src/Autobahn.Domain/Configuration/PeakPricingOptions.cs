namespace Autobahn.Domain.Configuration;

/// <summary>
/// A provider's peak hours: when a request it serves costs its model's rate card times
/// <see cref="Multiplier"/>. Configured under <c>Billing:PeakPricing:{provider}</c>.
/// </summary>
/// <remarks>
/// The rate card holds the off-peak price. DeepSeek, for one, bills twice that on weekday
/// mornings in UTC, and a single price per model cannot be right at both times of day.
/// </remarks>
public sealed class PeakPricingOptions
{
    /// <summary>Peak price over the rate card's price. 1 prices peak like off-peak.</summary>
    public decimal Multiplier { get; set; } = 1m;

    /// <summary>
    /// When the provider started billing peak hours. A request before it is priced at the rate card
    /// at every hour, so repricing older usage does not charge it a peak rate that did not exist yet.
    /// Null for always.
    /// </summary>
    public DateTimeOffset? EffectiveFromUtc { get; set; }

    /// <summary>The days of the week, in UTC, that have peak hours.</summary>
    public List<DayOfWeek> Days { get; set; } = [];

    /// <summary>The peak hours in UTC, each <c>HH:mm-HH:mm</c>: the start included, the end not.</summary>
    public List<string> WindowsUtc { get; set; } = [];

    /// <summary>
    /// The holiday calendar whose days off have no peak hours (<c>china</c> for DeepSeek). Its dates
    /// are matched against the request's UTC date, which for a window that falls inside the same
    /// day in the provider's own time zone is also the provider's date. Null for none.
    /// </summary>
    public string? HolidayCalendar { get; set; }

    /// <summary>
    /// More days with no peak hours, as UTC dates, beside the calendar's: a holiday it does not
    /// carry yet, or the provider's own.
    /// </summary>
    public List<DateOnly> ExcludedDates { get; set; } = [];
}
