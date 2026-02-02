using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Shifts;

public class RequestModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public RequestModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int ShiftId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Date { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? TimeSlotId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Slot { get; set; } = "Primary";

    public SlotType SlotType => Slot switch
    {
        "Backup1" => SlotType.Backup1,
        "Backup2" => SlotType.Backup2,
        _ => SlotType.Primary
    };

    public Shift? Shift { get; set; }
    public bool IsVirtualShift { get; set; }
    public bool RequestSubmitted { get; set; }
    public bool SlotUnavailable { get; set; }
    public bool AlreadyRequested { get; set; }

    private const string EmailCookieName = "vsms_volunteer_email";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = "";

        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Phone]
        public string? Phone { get; set; }
    }

    public async Task OnGetAsync()
    {
        if (ShiftId > 0)
        {
            // Existing shift in database
            Shift = await _dbContext.Shifts
                .Include(s => s.TimeSlot)
                .Include(s => s.Volunteer)
                .FirstOrDefaultAsync(s => s.Id == ShiftId);
        }
        else if (Date.HasValue && TimeSlotId.HasValue)
        {
            // Virtual shift - check if one exists for this date/timeslot
            Shift = await _dbContext.Shifts
                .Include(s => s.TimeSlot)
                .Include(s => s.Volunteer)
                .FirstOrDefaultAsync(s => s.Date == Date.Value && s.TimeSlotId == TimeSlotId.Value);

            if (Shift == null)
            {
                // Create virtual shift for display
                var timeSlot = await _dbContext.TimeSlots.FindAsync(TimeSlotId.Value);
                if (timeSlot != null)
                {
                    Shift = new Shift
                    {
                        Date = Date.Value,
                        TimeSlotId = TimeSlotId.Value,
                        TimeSlot = timeSlot,
                        Status = ShiftStatus.Open,
                        Role = ShiftRole.InPerson
                    };
                    IsVirtualShift = true;
                }
            }
        }

        if (Shift != null && !IsVirtualShift)
        {
            SlotUnavailable = !IsSlotAvailable(Shift, SlotType);

            // Check if user already requested this slot (using saved email cookie)
            var savedEmail = Request.Cookies[EmailCookieName];
            if (!string.IsNullOrEmpty(savedEmail))
            {
                Input.Email = savedEmail;
                var volunteer = await _dbContext.Volunteers
                    .FirstOrDefaultAsync(v => v.Email.ToLower() == savedEmail.ToLower());

                if (volunteer != null)
                {
                    Input.Name = volunteer.Name;
                    Input.Phone = volunteer.Phone;

                    if (Shift.Id > 0)
                    {
                        AlreadyRequested = await _dbContext.ShiftRequests
                            .AnyAsync(r => r.ShiftId == ShiftId &&
                                          r.VolunteerId == volunteer.Id &&
                                          r.RequestedSlot == SlotType &&
                                          r.Status == RequestStatus.Pending);
                    }
                }
            }
        }
        else if (Shift != null)
        {
            // Virtual shift - still try to prefill from cookie
            var savedEmail = Request.Cookies[EmailCookieName];
            if (!string.IsNullOrEmpty(savedEmail))
            {
                Input.Email = savedEmail;
                var volunteer = await _dbContext.Volunteers
                    .FirstOrDefaultAsync(v => v.Email.ToLower() == savedEmail.ToLower());

                if (volunteer != null)
                {
                    Input.Name = volunteer.Name;
                    Input.Phone = volunteer.Phone;
                }
            }
        }
    }

    private static bool IsSlotAvailable(Shift shift, SlotType slot) => slot switch
    {
        SlotType.Primary => shift.VolunteerId == null,
        SlotType.Backup1 => shift.Backup1VolunteerId == null,
        SlotType.Backup2 => shift.Backup2VolunteerId == null,
        _ => false
    };

    public async Task<IActionResult> OnPostAsync(int shiftId, DateOnly? date, int? timeSlotId, string slot)
    {
        Slot = slot ?? "Primary";

        // Try to find existing shift
        if (shiftId > 0)
        {
            Shift = await _dbContext.Shifts
                .Include(s => s.TimeSlot)
                .Include(s => s.Volunteer)
                .FirstOrDefaultAsync(s => s.Id == shiftId);
        }
        else if (date.HasValue && timeSlotId.HasValue)
        {
            // Check if shift was created since page load
            Shift = await _dbContext.Shifts
                .Include(s => s.TimeSlot)
                .Include(s => s.Volunteer)
                .FirstOrDefaultAsync(s => s.Date == date.Value && s.TimeSlotId == timeSlotId.Value);

            if (Shift == null)
            {
                // Create the shift now
                var timeSlot = await _dbContext.TimeSlots.FindAsync(timeSlotId.Value);
                if (timeSlot == null)
                {
                    return NotFound();
                }

                Shift = new Shift
                {
                    Date = date.Value,
                    TimeSlotId = timeSlotId.Value,
                    TimeSlot = timeSlot,
                    Status = ShiftStatus.Open,
                    Role = ShiftRole.InPerson
                };
                _dbContext.Shifts.Add(Shift);
                await _dbContext.SaveChangesAsync();
            }
        }

        if (Shift == null)
        {
            return NotFound();
        }

        // Check if the requested slot is available
        if (!IsSlotAvailable(Shift, SlotType))
        {
            SlotUnavailable = true;
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Find or create volunteer
        var volunteer = await _dbContext.Volunteers
            .FirstOrDefaultAsync(v => v.Email.ToLower() == Input.Email.ToLower());

        if (volunteer == null)
        {
            volunteer = new Volunteer
            {
                Name = Input.Name,
                Email = Input.Email,
                Phone = Input.Phone,
                IsActive = true
            };
            _dbContext.Volunteers.Add(volunteer);
            await _dbContext.SaveChangesAsync();
        }
        else
        {
            // Update name/phone if provided
            if (!string.IsNullOrEmpty(Input.Name))
                volunteer.Name = Input.Name;
            if (!string.IsNullOrEmpty(Input.Phone))
                volunteer.Phone = Input.Phone;
            await _dbContext.SaveChangesAsync();
        }

        // Save email to cookie for future visits
        Response.Cookies.Append(EmailCookieName, Input.Email, new CookieOptions
        {
            Expires = DateTimeOffset.Now.AddYears(1),
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax
        });

        // Check for existing pending request for this slot
        var existingRequest = await _dbContext.ShiftRequests
            .AnyAsync(r => r.ShiftId == Shift.Id &&
                           r.VolunteerId == volunteer.Id &&
                           r.RequestedSlot == SlotType &&
                           r.Status == RequestStatus.Pending);

        if (existingRequest)
        {
            ModelState.AddModelError("", "You have already requested this slot.");
            return Page();
        }

        // Create the request
        var request = new ShiftRequest
        {
            ShiftId = Shift.Id,
            VolunteerId = volunteer.Id,
            RequestedSlot = SlotType,
            Status = RequestStatus.Pending,
            RequestedAt = DateTime.UtcNow
        };

        _dbContext.ShiftRequests.Add(request);

        var slotLabel = SlotType switch
        {
            SlotType.Backup1 => "Backup 1",
            SlotType.Backup2 => "Backup 2",
            _ => "Primary"
        };

        // Log the action
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            ShiftId = Shift.Id,
            VolunteerId = volunteer.Id,
            Action = "Shift Requested",
            Details = $"{volunteer.Name} requested {slotLabel} slot on {Shift.Date:MMM d}"
        });

        await _dbContext.SaveChangesAsync();

        // TODO: Send notification email to admin

        RequestSubmitted = true;
        return Page();
    }
}
