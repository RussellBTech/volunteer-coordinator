using VolunteerCoordinator.Domain.Schedules;

namespace VolunteerCoordinator.Application.Time;

public static class RecurringCalendar
{
    public static IReadOnlyList<DateOnly> EnumerateDates(
        RecurringShiftSeriesRevision revision,
        DateOnly fromLocalDate,
        DateOnly throughLocalDate)
    {
        if (throughLocalDate < fromLocalDate)
        {
            return [];
        }

        var first = fromLocalDate < revision.AnchorLocalDate
            ? revision.AnchorLocalDate
            : fromLocalDate;
        var dates = new List<DateOnly>();
        for (var date = first; date <= throughLocalDate; date = date.AddDays(1))
        {
            if (revision.IncludesDate(date))
            {
                dates.Add(date);
            }
        }

        return dates;
    }

    public static DayOfWeekMask ToMask(IEnumerable<DayOfWeek> weekdays)
    {
        var result = DayOfWeekMask.None;
        foreach (var weekday in weekdays.Distinct())
        {
            result |= weekday.ToMask();
        }

        return result;
    }

    public static IReadOnlyList<DayOfWeek> ToWeekdays(DayOfWeekMask mask) =>
        Enum.GetValues<DayOfWeek>()
            .Where(day => mask.Contains(day))
            .OrderBy(day => day == DayOfWeek.Sunday ? 7 : (int)day)
            .ToArray();
}
