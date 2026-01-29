using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.Calendar;

public class EditShiftModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public EditShiftModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int ShiftId { get; set; }

    public Shift? Shift { get; set; }
    public List<Volunteer> Volunteers { get; set; } = new();

    public async Task OnGetAsync()
    {
        Shift = await _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .Include(s => s.Volunteer)
            .FirstOrDefaultAsync(s => s.Id == ShiftId);

        Volunteers = await _dbContext.Volunteers
            .Where(v => v.IsActive)
            .OrderBy(v => v.Name)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync(int shiftId, int? volunteerId, ShiftStatus status)
    {
        var shift = await _dbContext.Shifts
            .Include(s => s.Volunteer)
            .FirstOrDefaultAsync(s => s.Id == shiftId);

        if (shift == null)
        {
            return NotFound();
        }

        var oldVolunteerId = shift.VolunteerId;
        var oldStatus = shift.Status;

        shift.VolunteerId = volunteerId;
        shift.Status = status;

        // Update timestamps
        if (volunteerId != oldVolunteerId)
        {
            if (volunteerId != null)
            {
                shift.AssignedAt = DateTime.UtcNow;
                if (status == ShiftStatus.Open)
                {
                    shift.Status = ShiftStatus.Assigned;
                }
            }
            else
            {
                shift.AssignedAt = null;
                shift.ConfirmedAt = null;
                shift.Status = ShiftStatus.Open;
            }
        }

        if (status == ShiftStatus.Confirmed && oldStatus != ShiftStatus.Confirmed)
        {
            shift.ConfirmedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        // Log the change
        var adminEmail = User.FindFirstValue(ClaimTypes.Email);
        var admin = await _dbContext.AdminUsers.FirstOrDefaultAsync(a => a.Email == adminEmail);

        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            ShiftId = shift.Id,
            VolunteerId = shift.VolunteerId,
            AdminUserId = admin?.Id,
            Action = "Shift Updated",
            Details = $"Updated shift on {shift.Date:MMM d}: Status={shift.Status}, Volunteer={(volunteerId.HasValue ? "assigned" : "unassigned")}"
        });
        await _dbContext.SaveChangesAsync();

        return new OkResult();
    }
}
