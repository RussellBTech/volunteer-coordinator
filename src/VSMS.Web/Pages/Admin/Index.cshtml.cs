using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public IndexModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public int OpenShiftsThisWeek { get; set; }
    public int PendingRequests { get; set; }
    public int UnconfirmedShifts { get; set; }
    public List<Shift> UpcomingOpenShifts { get; set; } = new();
    public List<AuditLogEntry> RecentActivity { get; set; } = new();

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekFromNow = today.AddDays(7);

        OpenShiftsThisWeek = await _dbContext.Shifts
            .CountAsync(s => s.Status == ShiftStatus.Open && s.Date >= today && s.Date <= weekFromNow);

        PendingRequests = await _dbContext.ShiftRequests
            .CountAsync(r => r.Status == RequestStatus.Pending);

        UnconfirmedShifts = await _dbContext.Shifts
            .CountAsync(s => s.Status == ShiftStatus.Assigned && s.Date >= today);

        UpcomingOpenShifts = await _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .Where(s => s.Status == ShiftStatus.Open && s.Date >= today && s.Date <= weekFromNow)
            .OrderBy(s => s.Date)
            .ThenBy(s => s.TimeSlot.SortOrder)
            .Take(10)
            .ToListAsync();

        RecentActivity = await _dbContext.AuditLogEntries
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .ToListAsync();
    }
}
