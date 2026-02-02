using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Shifts;

public class OpenModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public OpenModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<(Shift Shift, string AvailableSlot)> OpenShifts { get; set; } = new();

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Get shifts that have any open slot (primary or backup)
        var shifts = await _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .Include(s => s.Volunteer)
            .Where(s => s.Date >= today &&
                       (s.VolunteerId == null || s.Backup1VolunteerId == null || s.Backup2VolunteerId == null))
            .OrderBy(s => s.Date)
            .ThenBy(s => s.TimeSlot.SortOrder)
            .Take(100)
            .ToListAsync();

        // Map to tuples with available slot info
        OpenShifts = shifts.SelectMany(s =>
        {
            var slots = new List<(Shift Shift, string AvailableSlot)>();
            if (s.VolunteerId == null)
                slots.Add((s, "Primary"));
            if (s.Backup1VolunteerId == null)
                slots.Add((s, "Backup 1"));
            if (s.Backup2VolunteerId == null)
                slots.Add((s, "Backup 2"));
            return slots;
        })
        .Take(50)
        .ToList();
    }
}
