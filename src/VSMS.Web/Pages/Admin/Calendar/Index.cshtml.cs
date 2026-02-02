using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.Calendar;

public class IndexModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public IndexModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    public string MonthYear => new DateTime(Year ?? DateTime.Today.Year, Month ?? DateTime.Today.Month, 1).ToString("MMMM yyyy");
    public DateOnly PreviousMonth => new DateOnly(Year ?? DateTime.Today.Year, Month ?? DateTime.Today.Month, 1).AddMonths(-1);
    public DateOnly NextMonth => new DateOnly(Year ?? DateTime.Today.Year, Month ?? DateTime.Today.Month, 1).AddMonths(1);

    public List<List<DateOnly?>> Weeks { get; set; } = new();
    public List<Shift> Shifts { get; set; } = new();

    public async Task OnGetAsync()
    {
        Month ??= DateTime.Today.Month;
        Year ??= DateTime.Today.Year;

        var firstDay = new DateOnly(Year.Value, Month.Value, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        // Build weeks grid
        var currentDate = firstDay.AddDays(-(int)firstDay.DayOfWeek);
        while (currentDate <= lastDay || currentDate.DayOfWeek != DayOfWeek.Sunday)
        {
            var week = new List<DateOnly?>();
            for (int i = 0; i < 7; i++)
            {
                if (currentDate.Month == Month.Value)
                    week.Add(currentDate);
                else
                    week.Add(null);

                currentDate = currentDate.AddDays(1);
            }
            Weeks.Add(week);
        }

        // Load shifts for this month
        Shifts = await _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .Include(s => s.Volunteer)
            .Include(s => s.Backup1Volunteer)
            .Include(s => s.Backup2Volunteer)
            .Where(s => s.Date >= firstDay && s.Date <= lastDay)
            .OrderBy(s => s.Date)
            .ThenBy(s => s.TimeSlot.SortOrder)
            .ToListAsync();
    }

    public List<Shift> GetShiftsForDate(DateOnly date)
    {
        return Shifts.Where(s => s.Date == date).ToList();
    }
}
