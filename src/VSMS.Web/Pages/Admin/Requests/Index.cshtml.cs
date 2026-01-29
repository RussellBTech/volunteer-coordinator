using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.Requests;

public class IndexModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public IndexModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "pending";

    public List<ShiftRequest> Requests { get; set; } = new();
    public int PendingCount { get; set; }

    public async Task OnGetAsync()
    {
        var requestStatus = Status switch
        {
            "approved" => RequestStatus.Approved,
            "rejected" => RequestStatus.Rejected,
            _ => RequestStatus.Pending
        };

        Requests = await _dbContext.ShiftRequests
            .Include(r => r.Volunteer)
            .Include(r => r.Shift)
                .ThenInclude(s => s.TimeSlot)
            .Include(r => r.ResolvedByAdmin)
            .Where(r => r.Status == requestStatus)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync();

        PendingCount = await _dbContext.ShiftRequests
            .CountAsync(r => r.Status == RequestStatus.Pending);
    }

    public async Task<IActionResult> OnPostAsync(int requestId, string action)
    {
        var request = await _dbContext.ShiftRequests
            .Include(r => r.Shift)
            .Include(r => r.Volunteer)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
        {
            return NotFound();
        }

        var adminEmail = User.FindFirstValue(ClaimTypes.Email);
        var admin = await _dbContext.AdminUsers.FirstOrDefaultAsync(a => a.Email == adminEmail);

        if (action == "approve")
        {
            // Only approve if shift is still open
            if (request.Shift.Status != ShiftStatus.Open)
            {
                TempData["Error"] = "This shift is no longer available.";
                return RedirectToPage();
            }

            request.Status = RequestStatus.Approved;
            request.ResolvedAt = DateTime.UtcNow;
            request.ResolvedByAdminId = admin?.Id;

            // Assign the volunteer to the shift
            request.Shift.VolunteerId = request.VolunteerId;
            request.Shift.Status = ShiftStatus.Assigned;
            request.Shift.AssignedAt = DateTime.UtcNow;

            // Log the action
            _dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                ShiftId = request.ShiftId,
                VolunteerId = request.VolunteerId,
                AdminUserId = admin?.Id,
                Action = "Shift Request Approved",
                Details = $"Approved request from {request.Volunteer.Name} for {request.Shift.Date:MMM d}"
            });

            TempData["Success"] = $"Request approved. {request.Volunteer.Name} has been assigned to the shift.";
        }
        else if (action == "reject")
        {
            request.Status = RequestStatus.Rejected;
            request.ResolvedAt = DateTime.UtcNow;
            request.ResolvedByAdminId = admin?.Id;

            _dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                ShiftId = request.ShiftId,
                VolunteerId = request.VolunteerId,
                AdminUserId = admin?.Id,
                Action = "Shift Request Rejected",
                Details = $"Rejected request from {request.Volunteer.Name}"
            });

            TempData["Success"] = "Request rejected.";
        }

        await _dbContext.SaveChangesAsync();

        return RedirectToPage(new { status = Status });
    }
}
