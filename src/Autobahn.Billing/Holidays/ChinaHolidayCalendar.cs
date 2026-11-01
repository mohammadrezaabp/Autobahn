using System.Collections.Concurrent;
using System.Globalization;
using Lunar.Util;
using Autobahn.Domain.Billing;

namespace Autobahn.Billing.Holidays;

/// <summary>
/// China's public holidays from the <c>lunar-csharp</c> package, which carries the State Council's
/// arrangement for each year it has been published for.
/// </summary>
/// <remarks>
/// The arrangement cannot be computed: the State Council stretches each festival around a weekend
/// and moves working days to make up for it, and announces the result late in the year before.
/// The package embeds each year's notice, so a new year arrives with a package update. A year it
/// has no notice for reads as having no days off, which the Providers page points out.
/// </remarks>
public sealed class ChinaHolidayCalendar : IHolidayCalendar
{
    private readonly ConcurrentDictionary<int, IReadOnlySet<DateOnly>> _years = new();

    public string Name => HolidayCalendars.China;

    public IReadOnlySet<DateOnly> DaysOff(int year) => _years.GetOrAdd(year, Load);

    private static IReadOnlySet<DateOnly> Load(int year)
    {
        var days = new HashSet<DateOnly>();
        foreach (var holiday in HolidayUtil.GetHolidays(year))
        {
            // Work marks a weekend day worked to make up for a holiday: not a day off.
            if (!holiday.Work &&
                DateOnly.TryParseExact(holiday.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) &&
                day.Year == year)
            {
                days.Add(day);
            }
        }

        return days;
    }
}
