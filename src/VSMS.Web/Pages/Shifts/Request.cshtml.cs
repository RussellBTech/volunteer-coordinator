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

    public Shift? Shift { get; set; }
    public bool RequestSubmitted { get; set; }

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
            .FirstOrDefaultAsync(s => s.Id == ShiftId);
    }

    public async Task<IActionResult> OnPostAsync(int shiftId)
    {
        Shift = await _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .FirstOrDefaultAsync(s => s.Id == shiftId);

        if (Shift == null)
        {
            return NotFound();
        }

        if (Shift.Status != ShiftStatus.Open)
        {
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
                IsActive = true,
                IsBackup = false
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

        // Check for existing pending request
        var existingRequest = await _dbContext.ShiftRequests
            .AnyAsync(r => r.ShiftId == shiftId &&
                           r.VolunteerId == volunteer.Id &&
                           r.Status == RequestStatus.Pending);

        if (existingRequest)
        {
            ModelState.AddModelError("", "You have already requested this shift.");
            return Page();
        }

        // Create the request
        var request = new ShiftRequest
        {
            ShiftId = shiftId,
            VolunteerId = volunteer.Id,
            Status = RequestStatus.Pending,
            RequestedAt = DateTime.UtcNow
        };

        _dbContext.ShiftRequests.Add(request);

        // Log the action
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            ShiftId = shiftId,
            VolunteerId = volunteer.Id,
            Action = "Shift Requested",
            Details = $"{volunteer.Name} requested shift on {Shift.Date:MMM d}"
        });

        await _dbContext.SaveChangesAsync();

        // TODO: Send notification email to admin

        RequestSubmitted = true;
        return Page();
    }
}
