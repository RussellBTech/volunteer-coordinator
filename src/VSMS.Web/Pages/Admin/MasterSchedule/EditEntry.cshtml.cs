using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.MasterSchedule;

public class EditEntryModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public EditEntryModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public DayOfWeek DayOfWeek { get; set; }

    [BindProperty(SupportsGet = true)]
    public int TimeSlotId { get; set; }

    [BindProperty(SupportsGet = true)]
    public ShiftRole Role { get; set; }

    public TimeSlot? TimeSlot { get; set; }
    public MasterScheduleEntry? Entry { get; set; }
    public List<Volunteer> Volunteers { get; set; } = new();

    public string DayName => DayOfWeek.ToString();

    public async Task OnGetAsync()
    {
        TimeSlot = await _dbContext.TimeSlots.FindAsync(TimeSlotId);

        Entry = await _dbContext.MasterScheduleEntries
            .FirstOrDefaultAsync(e =>
                e.DayOfWeek == DayOfWeek &&
                e.TimeSlotId == TimeSlotId &&
                e.Role == Role);

        Volunteers = await _dbContext.Volunteers
            .Where(v => v.IsActive)
            .OrderBy(v => v.Name)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync(int? volunteerId, bool isClosed = false)
    {
        var entry = await _dbContext.MasterScheduleEntries
            .FirstOrDefaultAsync(e =>
                e.DayOfWeek == DayOfWeek &&
                e.TimeSlotId == TimeSlotId &&
                e.Role == Role);

        if (entry == null)
        {
            entry = new MasterScheduleEntry
            {
                DayOfWeek = DayOfWeek,
                TimeSlotId = TimeSlotId,
                Role = Role
            };
            _dbContext.MasterScheduleEntries.Add(entry);
        }

        entry.DefaultVolunteerId = volunteerId;
        entry.IsClosed = isClosed;

        await _dbContext.SaveChangesAsync();

        return new OkResult();
    }
}
