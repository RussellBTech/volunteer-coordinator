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

    [BindProperty(SupportsGet = true)]
    public string? Date { get; set; }

    public DateOnly? SelectedDate { get; set; }
    public List<OpenShiftSlot> OpenShifts { get; set; } = new();
    public List<MonthCalendar> Months { get; set; } = new();

    public class MonthCalendar
    {
        public string MonthYear { get; set; } = "";
        public List<List<DateOnly?>> Weeks { get; set; } = new();
    }

    public class OpenShiftSlot
    {
        public int? ShiftId { get; set; }
        public DateOnly Date { get; set; }
        public TimeSlot TimeSlot { get; set; } = null!;
        public ShiftRole Role { get; set; }
        public string AvailableSlot { get; set; } = "Primary";
        public string? PrimaryVolunteerName { get; set; }
    }

    public List<OpenShiftSlot> GetSlotsForDate(DateOnly date)
    {
        return OpenShifts.Where(s => s.Date == date).ToList();
    }

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var threeMonthsOut = today.AddMonths(3);

        // Check if viewing a specific date
        if (!string.IsNullOrEmpty(Date) && DateOnly.TryParse(Date, out var parsedDate))
        {
            if (parsedDate >= today && parsedDate <= threeMonthsOut)
            {
                SelectedDate = parsedDate;
            }
        }

        // Build calendar for current month + next 2 months
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        for (int m = 0; m < 3; m++)
        {
            var month = currentMonth.AddMonths(m);
            var firstDay = new DateOnly(month.Year, month.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            var monthCal = new MonthCalendar
            {
                MonthYear = firstDay.ToString("MMMM yyyy")
            };

            // Build weeks for this month
            var currentDate = firstDay.AddDays(-(int)firstDay.DayOfWeek);
            while (currentDate <= lastDay || currentDate.DayOfWeek != DayOfWeek.Sunday)
            {
                var week = new List<DateOnly?>();
                for (int i = 0; i < 7; i++)
                {
                    if (currentDate.Month == month.Month && currentDate >= today)
                        week.Add(currentDate);
                    else
                        week.Add(null);
                    currentDate = currentDate.AddDays(1);
                }
                monthCal.Weeks.Add(week);
            }

            Months.Add(monthCal);
        }

        // Load active time slots
        var timeSlots = await _dbContext.TimeSlots
            .Where(ts => ts.IsActive)
            .OrderBy(ts => ts.SortOrder)
            .ToListAsync();

        // Load existing shifts for the next 3 months
        var existingShifts = await _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .Include(s => s.Volunteer)
            .Where(s => s.Date >= today && s.Date <= threeMonthsOut)
            .ToListAsync();

        var shiftLookup = existingShifts
            .ToDictionary(s => (s.Date, s.TimeSlotId));

        // Generate open slots for each day and time slot
        for (var date = today; date <= threeMonthsOut; date = date.AddDays(1))
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
