using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.MasterSchedule;

public class IndexModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public IndexModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<TimeSlot> TimeSlots { get; set; } = new();
    public List<MasterScheduleEntry> Entries { get; set; } = new();
    public string[] DaysOfWeek { get; } = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    public async Task OnGetAsync()
    {
        TimeSlots = await _dbContext.TimeSlots
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ToListAsync();

        Entries = await _dbContext.MasterScheduleEntries
            .Include(e => e.DefaultVolunteer)
            .ToListAsync();
    }

    public MasterScheduleEntry? GetEntry(DayOfWeek day, int timeSlotId, ShiftRole role)
    {
        return Entries.FirstOrDefault(e =>
            e.DayOfWeek == day &&
            e.TimeSlotId == timeSlotId &&
            e.Role == role);
    }
}
