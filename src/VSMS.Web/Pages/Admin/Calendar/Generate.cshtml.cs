using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.Calendar;

public class GenerateModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public GenerateModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int Month { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Year { get; set; }

    public string MonthYear => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);
    public bool AlreadyGenerated { get; set; }
    public int ExistingShiftsCount { get; set; }
    public int ShiftsToCreate { get; set; }
    public int ShiftsWithDefaults { get; set; }

    public async Task OnGetAsync()
    {
        if (Month == 0) Month = DateTime.Today.Month;
        if (Year == 0) Year = DateTime.Today.Year;

        var firstDay = new DateOnly(Year, Month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        ExistingShiftsCount = await _dbContext.Shifts
            .CountAsync(s => s.Date >= firstDay && s.Date <= lastDay);

        AlreadyGenerated = ExistingShiftsCount > 0;

        if (!AlreadyGenerated)
        {
            var masterSchedule = await _dbContext.MasterScheduleEntries
                .Where(e => !e.IsClosed)
                .ToListAsync();

            var timeSlots = await _dbContext.TimeSlots
                .Where(t => t.IsActive)
                .ToListAsync();

            // Calculate preview
            for (var date = firstDay; date <= lastDay; date = date.AddDays(1))
            {
                foreach (var timeSlot in timeSlots)
                {
                    foreach (var role in Enum.GetValues<ShiftRole>())
                    {
                        var entry = masterSchedule.FirstOrDefault(e =>
                            e.DayOfWeek == date.DayOfWeek &&
                            e.TimeSlotId == timeSlot.Id &&
                            e.Role == role);

                        // Skip if explicitly closed or no entry and we want to skip
                        if (entry?.IsClosed == true) continue;

                        // Only create shifts for days/slots that have master schedule entries
                        // or create all possible combinations
                        ShiftsToCreate++;
                        if (entry?.DefaultVolunteerId != null)
                            ShiftsWithDefaults++;
                    }
                }
            }
        }
    }

    public async Task<IActionResult> OnPostAsync(int month, int year)
    {
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        // Check for existing shifts
        var existingCount = await _dbContext.Shifts
            .CountAsync(s => s.Date >= firstDay && s.Date <= lastDay);

        if (existingCount > 0)
        {
            TempData["Error"] = "Shifts already exist for this month.";
            return RedirectToPage(new { month, year });
        }

        var masterSchedule = await _dbContext.MasterScheduleEntries
            .ToListAsync();

        var timeSlots = await _dbContext.TimeSlots
            .Where(t => t.IsActive)
            .ToListAsync();

        var shiftsCreated = 0;

        for (var date = firstDay; date <= lastDay; date = date.AddDays(1))
        {
            foreach (var timeSlot in timeSlots)
            {
                foreach (var role in Enum.GetValues<ShiftRole>())
                {
                    var entry = masterSchedule.FirstOrDefault(e =>
                        e.DayOfWeek == date.DayOfWeek &&
                        e.TimeSlotId == timeSlot.Id &&
                        e.Role == role);

                    // Skip if marked as closed
                    if (entry?.IsClosed == true) continue;

                    var shift = new Shift
                    {
                        Date = date,
                        TimeSlotId = timeSlot.Id,
                        Role = role,
                        VolunteerId = entry?.DefaultVolunteerId,
                        Status = entry?.DefaultVolunteerId != null
                            ? ShiftStatus.Assigned
                            : ShiftStatus.Open,
                        AssignedAt = entry?.DefaultVolunteerId != null
                            ? DateTime.UtcNow
                            : null
                    };

                    _dbContext.Shifts.Add(shift);
                    shiftsCreated++;
                }
            }
        }

        await _dbContext.SaveChangesAsync();

        // Log the generation
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            Action = "Month Generated",
            Details = $"Generated {shiftsCreated} shifts for {new DateTime(year, month, 1):MMMM yyyy}"
        });
        await _dbContext.SaveChangesAsync();

        TempData["Success"] = $"Generated {shiftsCreated} shifts for {new DateTime(year, month, 1):MMMM yyyy}.";
        return RedirectToPage("Index", new { month, year });
    }
}
