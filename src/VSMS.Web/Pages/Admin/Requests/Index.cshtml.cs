using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Core.Interfaces;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.Requests;

public class IndexModel : PageModel
{
    private readonly VsmsDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(VsmsDbContext dbContext, IEmailService emailService, ILogger<IndexModel> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _logger = logger;
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
                .ThenInclude(s => s.TimeSlot)
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
            // Check if the requested slot is still available
            var slotAvailable = request.RequestedSlot switch
            {
                SlotType.Primary => request.Shift.VolunteerId == null,
                SlotType.Backup1 => request.Shift.Backup1VolunteerId == null,
                SlotType.Backup2 => request.Shift.Backup2VolunteerId == null,
                _ => false
            };

            if (!slotAvailable)
            {
                TempData["Error"] = "This slot is no longer available.";
                return RedirectToPage();
            }

            request.Status = RequestStatus.Approved;
            request.ResolvedAt = DateTime.UtcNow;
            request.ResolvedByAdminId = admin?.Id;

            // Assign the volunteer to the appropriate slot
            var slotLabel = request.RequestedSlot switch
            {
                SlotType.Backup1 => "Backup 1",
                SlotType.Backup2 => "Backup 2",
                _ => "Primary"
            };

            switch (request.RequestedSlot)
            {
                case SlotType.Backup1:
                    request.Shift.Backup1VolunteerId = request.VolunteerId;
                    break;
                case SlotType.Backup2:
                    request.Shift.Backup2VolunteerId = request.VolunteerId;
                    break;
                default:
                    request.Shift.VolunteerId = request.VolunteerId;
                    request.Shift.Status = ShiftStatus.Assigned;
                    request.Shift.AssignedAt = DateTime.UtcNow;
                    break;
            }

            // Log the action
            _dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                ShiftId = request.ShiftId,
                VolunteerId = request.VolunteerId,
                AdminUserId = admin?.Id,
                Action = "Shift Request Approved",
                Details = $"Approved {slotLabel} request from {request.Volunteer.Name} for {request.Shift.Date:MMM d}"
            });

            TempData["Success"] = $"Request approved. {request.Volunteer.Name} has been assigned as {slotLabel}.";

            // Ensure TimeSlot is loaded for email
            if (request.Shift.TimeSlot == null)
            {
                await _dbContext.Entry(request.Shift).Reference(s => s.TimeSlot).LoadAsync();
            }

            // Send approval email to volunteer (non-fatal if it fails)
            try
            {
                await _emailService.SendShiftApprovedAsync(request.Volunteer, request.Shift);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send approval email to {Email}, but approval succeeded", request.Volunteer.Email);
                TempData["Warning"] = "Request approved, but notification email could not be sent.";
            }
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

            // Ensure TimeSlot is loaded for email
            if (request.Shift.TimeSlot == null)
            {
                await _dbContext.Entry(request.Shift).Reference(s => s.TimeSlot).LoadAsync();
            }

            // Send rejection email to volunteer (non-fatal if it fails)
            try
            {
                await _emailService.SendShiftRejectedAsync(request.Volunteer, request.Shift);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send rejection email to {Email}, but rejection succeeded", request.Volunteer.Email);
                TempData["Warning"] = "Request rejected, but notification email could not be sent.";
            }
        }

        await _dbContext.SaveChangesAsync();

        return RedirectToPage(new { status = Status });
    }
}
