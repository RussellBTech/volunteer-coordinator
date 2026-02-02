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
    public string Slot { get; set; } = "Primary";

    public SlotType SlotType => Slot switch
    {
        "Backup1" => SlotType.Backup1,
        "Backup2" => SlotType.Backup2,
        _ => SlotType.Primary
    };

    public Shift? Shift { get; set; }
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
        Shift = await _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .Include(s => s.Volunteer)
            .FirstOrDefaultAsync(s => s.Id == ShiftId);

        if (Shift != null)
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

                    AlreadyRequested = await _dbContext.ShiftRequests
                        .AnyAsync(r => r.ShiftId == ShiftId &&
                                      r.VolunteerId == volunteer.Id &&
                                      r.RequestedSlot == SlotType &&
                                      r.Status == RequestStatus.Pending);
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

    public async Task<IActionResult> OnPostAsync(int shiftId, string slot)
    {
        Slot = slot ?? "Primary";

        Shift = await _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .Include(s => s.Volunteer)
            .FirstOrDefaultAsync(s => s.Id == shiftId);

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

        // Check for existing pending request for this slot
        var existingRequest = await _dbContext.ShiftRequests
            .AnyAsync(r => r.ShiftId == shiftId &&
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
            ShiftId = shiftId,
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
            ShiftId = shiftId,
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
