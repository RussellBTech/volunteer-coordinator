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

    public List<OpenShiftSlot> OpenShifts { get; set; } = new();

    public class OpenShiftSlot
    {
        public int? ShiftId { get; set; }
        public DateOnly Date { get; set; }
        public TimeSlot TimeSlot { get; set; } = null!;
        public ShiftRole Role { get; set; }
        public string AvailableSlot { get; set; } = "Primary";
        public string? PrimaryVolunteerName { get; set; }
    }

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var twoWeeksOut = today.AddDays(14);

        // Load active time slots
        var timeSlots = await _dbContext.TimeSlots
            .Where(ts => ts.IsActive)
            .OrderBy(ts => ts.SortOrder)
            .ToListAsync();

        // Load existing shifts for the next 2 weeks
        var existingShifts = await _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .Include(s => s.Volunteer)
            .Where(s => s.Date >= today && s.Date <= twoWeeksOut)
            .ToListAsync();

        var shiftLookup = existingShifts
            .ToDictionary(s => (s.Date, s.TimeSlotId));

        // Generate open slots for each day and time slot
        for (var date = today; date <= twoWeeksOut; date = date.AddDays(1))
        {
            foreach (var timeSlot in timeSlots)
            {
                shiftLookup.TryGetValue((date, timeSlot.Id), out var existingShift);

                // Check primary slot
                if (existingShift?.VolunteerId == null)
                {
                    OpenShifts.Add(new OpenShiftSlot
                    {
                        ShiftId = existingShift?.Id,
                        Date = date,
                        TimeSlot = timeSlot,
                        Role = existingShift?.Role ?? ShiftRole.InPerson,
                        AvailableSlot = "Primary",
                        PrimaryVolunteerName = null
                    });
                }

                // Check backup slots (only if shift exists in DB with a primary)
                if (existingShift != null && existingShift.VolunteerId != null)
                {
                    if (existingShift.Backup1VolunteerId == null)
                    {
                        OpenShifts.Add(new OpenShiftSlot
                        {
                            ShiftId = existingShift.Id,
                            Date = date,
                            TimeSlot = timeSlot,
                            Role = existingShift.Role,
                            AvailableSlot = "Backup1",
                            PrimaryVolunteerName = existingShift.Volunteer?.Name
                        });
                    }
                    if (existingShift.Backup2VolunteerId == null)
                    {
                        OpenShifts.Add(new OpenShiftSlot
                        {
                            ShiftId = existingShift.Id,
                            Date = date,
                            TimeSlot = timeSlot,
                            Role = existingShift.Role,
                            AvailableSlot = "Backup2",
                            PrimaryVolunteerName = existingShift.Volunteer?.Name
                        });
                    }
                }
            }
        }
    }
}
