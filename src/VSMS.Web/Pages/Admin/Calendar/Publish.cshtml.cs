using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.Calendar;

public class PublishModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public PublishModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int Month { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Year { get; set; }

    public string MonthYear => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    public bool AlreadyPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int TotalShifts { get; set; }
    public int AssignedShifts { get; set; }
    public int OpenShifts { get; set; }
    public int VolunteersToNotify { get; set; }

    public async Task OnGetAsync()
    {
        if (Month == 0) Month = DateTime.Today.Month;
        if (Year == 0) Year = DateTime.Today.Year;

        var firstDay = new DateOnly(Year, Month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        var shifts = await _dbContext.Shifts
            .Where(s => s.Date >= firstDay && s.Date <= lastDay)
            .ToListAsync();

        TotalShifts = shifts.Count;
        AssignedShifts = shifts.Count(s => s.Status != ShiftStatus.Open);
        OpenShifts = shifts.Count(s => s.Status == ShiftStatus.Open);

        var published = shifts.FirstOrDefault(s => s.MonthPublishedAt != null);
        AlreadyPublished = published != null;
        PublishedAt = published?.MonthPublishedAt;

        VolunteersToNotify = await _dbContext.Shifts
            .Where(s => s.Date >= firstDay && s.Date <= lastDay && s.VolunteerId != null)
            .Select(s => s.VolunteerId)
            .Distinct()
            .CountAsync();
    }

    public async Task<IActionResult> OnPostAsync(int month, int year)
    {
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);
        var now = DateTime.UtcNow;

        var shifts = await _dbContext.Shifts
            .Where(s => s.Date >= firstDay && s.Date <= lastDay)
            .ToListAsync();

        foreach (var shift in shifts)
        {
            shift.MonthPublishedAt = now;
        }

        await _dbContext.SaveChangesAsync();

        // Log the publish
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            Action = "Month Published",
            Details = $"Published {shifts.Count} shifts for {new DateTime(year, month, 1):MMMM yyyy}"
        });
        await _dbContext.SaveChangesAsync();

        // TODO: Trigger email sending via background job
        // TODO: Trigger Google Calendar sync

        TempData["Success"] = $"Published {shifts.Count} shifts for {new DateTime(year, month, 1):MMMM yyyy}. Email notifications will be sent shortly.";
        return RedirectToPage("Index", new { month, year });
    }
}
