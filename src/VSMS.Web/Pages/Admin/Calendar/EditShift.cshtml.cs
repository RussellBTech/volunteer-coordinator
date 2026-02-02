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
            .Include(s => s.Backup1Volunteer)
            .Include(s => s.Backup2Volunteer)
            .FirstOrDefaultAsync(s => s.Id == ShiftId);

        Volunteers = await _dbContext.Volunteers
            .Where(v => v.IsActive)
            .OrderBy(v => v.Name)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync(int shiftId, int? volunteerId, int? backup1VolunteerId, int? backup2VolunteerId, ShiftRole role)
    {
        var shift = await _dbContext.Shifts
            .Include(s => s.Volunteer)
            .Include(s => s.TimeSlot)
            .FirstOrDefaultAsync(s => s.Id == shiftId);

        if (shift == null)
        {
            return NotFound();
        }

        var oldVolunteerId = shift.VolunteerId;

        shift.VolunteerId = volunteerId;
        shift.Backup1VolunteerId = backup1VolunteerId;
        shift.Backup2VolunteerId = backup2VolunteerId;
        shift.Role = role;

        // Auto-manage status based on volunteer assignment
        if (volunteerId != oldVolunteerId)
        {
            if (volunteerId != null)
            {
                shift.AssignedAt = DateTime.UtcNow;
                if (shift.Status == ShiftStatus.Open)
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
            Details = $"Updated shift on {shift.Date:MMM d}: Status={shift.Status}"
        });
        await _dbContext.SaveChangesAsync();

        // Reload volunteers for display
        if (shift.VolunteerId.HasValue)
        {
            shift.Volunteer = await _dbContext.Volunteers.FindAsync(shift.VolunteerId.Value);
        }
        if (shift.Backup1VolunteerId.HasValue)
        {
            shift.Backup1Volunteer = await _dbContext.Volunteers.FindAsync(shift.Backup1VolunteerId.Value);
        }
        if (shift.Backup2VolunteerId.HasValue)
        {
            shift.Backup2Volunteer = await _dbContext.Volunteers.FindAsync(shift.Backup2VolunteerId.Value);
        }

        // Return HTML fragment for out-of-band swap of the calendar cell
        var statusClass = shift.Status switch
        {
            ShiftStatus.Open => "shift-open",
            ShiftStatus.Assigned => "shift-assigned",
            ShiftStatus.Confirmed => "shift-confirmed",
            _ => ""
        };

        var volunteerName = shift.Volunteer?.Name.Split(' ')[0] ?? "";
        var volunteerHtml = !string.IsNullOrEmpty(volunteerName) ? $"<br />{volunteerName}" : "";
        var backupCount = (shift.Backup1VolunteerId != null ? 1 : 0) + (shift.Backup2VolunteerId != null ? 1 : 0);
        var backupHtml = backupCount > 0 ? $@"<span class=""badge bg-secondary"" style=""font-size: 0.6rem;"">+{backupCount}</span>" : "";

        var cellHtml = $@"<div id=""shift-cell-{shift.Id}""
             class=""small p-1 mb-1 rounded {statusClass}""
             hx-get=""/admin/calendar/edit-shift?shiftId={shift.Id}""
             hx-target=""#shift-modal-content""
             hx-trigger=""click""
             data-bs-toggle=""modal""
             data-bs-target=""#shiftModal""
             style=""cursor: pointer; font-size: 0.75rem;"">
            <strong>{shift.TimeSlot.StartTime:h:mm}</strong>
            {shift.Role.ToString()[0]}{backupHtml}{volunteerHtml}
        </div>";

        return Content(cellHtml, "text/html");
    }
}
