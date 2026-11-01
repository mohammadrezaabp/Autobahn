namespace Autobahn.Domain.Billing;
/// <summary>
/// A country's official days off, as its government announces them a year at a time: the holidays
/// and the weekdays joined to them, not the weekend days worked to make up for them.
/// </summary>
public interface IHolidayCalendar
{
    /// <summary>The name configuration refers to it by; one of <see cref = "HolidayCalendars"/>.</summary>
    string Name { get; }

    /// <summary>The official days off in <paramref name = "year"/>; empty for a year the calendar has no schedule for yet.</summary>
    IReadOnlySet<DateOnly> DaysOff(int year);
}
