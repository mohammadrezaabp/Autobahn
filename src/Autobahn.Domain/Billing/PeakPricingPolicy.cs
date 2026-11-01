using System.Globalization;
using Autobahn.Domain.Configuration;

namespace Autobahn.Domain.Billing;

/// <summary>
/// Decides whether a request a provider served ran in its peak hours, and prices the model's rate
/// card for it.
/// </summary>
/// <remarks>
/// Only the token prices move. A per-request price is a separate charge with no time of day.
/// A provider with no schedule is priced at its rate card at every hour.
/// </remarks>
public sealed class PeakPricingPolicy
{
    public static PeakPricingPolicy None { get; } = new(new Dictionary<string, PeakPricingOptions>());

    private readonly Dictionary<string, Schedule> _schedules = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="calendars">The holiday calendars a schedule may name; one it names and is not given is an error.</param>
    public PeakPricingPolicy(
        IReadOnlyDictionary<string, PeakPricingOptions> configured,
        IEnumerable<IHolidayCalendar>? calendars = null)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var byName = (calendars ?? []).ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (provider, options) in configured)
        {
            if (string.IsNullOrWhiteSpace(provider) || options is null || options.Multiplier == 1m)
            {
                continue;
            }

            var windows = new List<(int Start, int End)>(options.WindowsUtc.Count);
            foreach (var text in options.WindowsUtc)
            {
                if (!TryParseWindow(text, out var start, out var end))
                {
                    throw new ArgumentException(
                        $"Billing:PeakPricing:{provider}: '{text}' is not a window of the form HH:mm-HH:mm.",
                        nameof(configured));
                }

                windows.Add((start, end));
            }

            IHolidayCalendar? calendar = null;
            if (!string.IsNullOrWhiteSpace(options.HolidayCalendar) &&
                !byName.TryGetValue(options.HolidayCalendar.Trim(), out calendar))
            {
                throw new ArgumentException(
                    $"Billing:PeakPricing:{provider}: there is no holiday calendar '{options.HolidayCalendar}'.",
                    nameof(configured));
            }

            _schedules[provider.Trim()] = new Schedule(
                options.Multiplier,
                options.EffectiveFromUtc,
                [.. options.Days],
                windows,
                calendar,
                [.. options.ExcludedDates]);
        }
    }

    /// <summary>What a request the provider served at <paramref name="atUtc"/> costs over its rate card's price.</summary>
    public decimal MultiplierAt(string? provider, DateTimeOffset atUtc)
    {
        if (string.IsNullOrWhiteSpace(provider) || !_schedules.TryGetValue(provider, out var schedule))
        {
            return 1m;
        }

        if (atUtc < schedule.EffectiveFromUtc)
        {
            return 1m;
        }

        var utc = atUtc.UtcDateTime;
        if (!schedule.Days.Contains(utc.DayOfWeek) || schedule.IsDayOff(DateOnly.FromDateTime(utc)))
        {
            return 1m;
        }

        var minute = (utc.Hour * 60) + utc.Minute;
        foreach (var (start, end) in schedule.Windows)
        {
            if (minute >= start && minute < end)
            {
                return schedule.Multiplier;
            }
        }

        return 1m;
    }

    /// <summary>
    /// The rate card as it applies to a request the provider served at <paramref name="atUtc"/>:
    /// the card itself off-peak, its token prices times the multiplier at peak.
    /// </summary>
    public RateCardRecord Apply(RateCardRecord rateCard, string? provider, DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(rateCard);
        var multiplier = MultiplierAt(provider, atUtc);
        return multiplier == 1m
            ? rateCard
            : rateCard with
            {
                InputPricePerMillionTokens = rateCard.InputPricePerMillionTokens * multiplier,
                OutputPricePerMillionTokens = rateCard.OutputPricePerMillionTokens * multiplier,
                CachedInputPricePerMillionTokens = rateCard.CachedInputPricePerMillionTokens * multiplier,
            };
    }

    /// <summary>
    /// When a request started: its ledger time less how long it ran. The ledger records a request
    /// when it ends, and a provider prices it by when it ran, so a request that started at 03:59
    /// and ended at 04:01 is a peak one.
    /// </summary>
    public static DateTimeOffset StartOf(DateTimeOffset recordedAt, double durationMs) =>
        durationMs > 0 && double.IsFinite(durationMs) ? recordedAt - TimeSpan.FromMilliseconds(durationMs) : recordedAt;

    /// <summary>The schedule of a provider that has one, for showing it; null for flat pricing.</summary>
    public PeakPricingOptions? Describe(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider) || !_schedules.TryGetValue(provider, out var schedule))
        {
            return null;
        }

        return new PeakPricingOptions
        {
            Multiplier = schedule.Multiplier,
            EffectiveFromUtc = schedule.EffectiveFromUtc,
            Days = [.. schedule.Days.Order()],
            WindowsUtc = [.. schedule.Windows.Select(w => $"{FormatMinute(w.Start)}-{FormatMinute(w.End)}")],
            HolidayCalendar = schedule.Calendar?.Name,
            ExcludedDates = [.. schedule.ExcludedDates.Order()],
        };
    }

    /// <summary>
    /// The provider's days with no peak hours from <paramref name="from"/> to the end of the next
    /// year: its calendar's and its configured ones.
    /// </summary>
    public IReadOnlyList<DateOnly> UpcomingDaysOff(string provider, DateOnly from)
    {
        if (string.IsNullOrWhiteSpace(provider) || !_schedules.TryGetValue(provider, out var schedule))
        {
            return [];
        }

        var days = new SortedSet<DateOnly>(schedule.ExcludedDates.Where(d => d >= from && d.Year <= from.Year + 1));
        if (schedule.Calendar is { } calendar)
        {
            days.UnionWith(calendar.DaysOff(from.Year).Where(d => d >= from));
            days.UnionWith(calendar.DaysOff(from.Year + 1));
        }

        return [.. days];
    }

    /// <summary>
    /// Of this year and the next, those the provider's calendar has no holidays for yet: every
    /// weekday of such a year is priced with its peak hours. Empty for a schedule with no calendar.
    /// </summary>
    public IReadOnlyList<int> YearsWithoutHolidays(string provider, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(provider) ||
            !_schedules.TryGetValue(provider, out var schedule) ||
            schedule.Calendar is not { } calendar)
        {
            return [];
        }

        return [.. new[] { today.Year, today.Year + 1 }.Where(year => calendar.DaysOff(year).Count == 0)];
    }

    /// <summary>
    /// Reads <c>HH:mm-HH:mm</c> as minutes of the day. The end may be <c>24:00</c>; a window may not
    /// be empty or wrap past midnight (write two).
    /// </summary>
    public static bool TryParseWindow(string? text, out int startMinute, out int endMinute)
    {
        startMinute = endMinute = 0;
        var parts = text?.Split('-', StringSplitOptions.TrimEntries);
        return parts is { Length: 2 } &&
               TryParseMinute(parts[0], allowEndOfDay: false, out startMinute) &&
               TryParseMinute(parts[1], allowEndOfDay: true, out endMinute) &&
               startMinute < endMinute;
    }

    private static bool TryParseMinute(string text, bool allowEndOfDay, out int minute)
    {
        minute = 0;
        if (allowEndOfDay && text == "24:00")
        {
            minute = 24 * 60;
            return true;
        }

        if (!TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return false;
        }

        minute = (time.Hour * 60) + time.Minute;
        return true;
    }

    private static string FormatMinute(int minute) =>
        $"{minute / 60:00}:{minute % 60:00}";

    private sealed record Schedule(
        decimal Multiplier,
        DateTimeOffset? EffectiveFromUtc,
        HashSet<DayOfWeek> Days,
        IReadOnlyList<(int Start, int End)> Windows,
        IHolidayCalendar? Calendar,
        HashSet<DateOnly> ExcludedDates)
    {
        public bool IsDayOff(DateOnly date) =>
            ExcludedDates.Contains(date) || Calendar?.DaysOff(date.Year).Contains(date) == true;
    }
}
