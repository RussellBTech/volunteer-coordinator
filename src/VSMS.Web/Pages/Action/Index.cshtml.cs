using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Action;

public class IndexModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public IndexModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = "";

    public ActionToken? ActionToken { get; set; }
    public string? TokenError { get; set; }
    public string? TokenErrorDetails { get; set; }
    public bool ActionCompleted { get; set; }
    public string CompletedTitle { get; set; } = "";
    public string CompletedMessage { get; set; } = "";

    public string ActionTitle { get; set; } = "";
    public string ActionDescription { get; set; } = "";
    public string ButtonText { get; set; } = "";
    public string ButtonClass { get; set; } = "btn-primary";

    public async Task OnGetAsync()
    {
        await LoadToken();
    }

    private async Task LoadToken()
    {
        ActionToken = await _dbContext.ActionTokens
            .Include(t => t.Shift)
                .ThenInclude(s => s.TimeSlot)
            .Include(t => t.Shift)
                .ThenInclude(s => s.Volunteer)
            .Include(t => t.Volunteer)
            .FirstOrDefaultAsync(t => t.Token == Token);

        if (ActionToken == null)
        {
            TokenError = "Invalid Link";
            TokenErrorDetails = "This action link is invalid or has been removed.";
            return;
        }

        if (ActionToken.UsedAt != null)
        {
            TokenError = "Action Already Completed";
            TokenErrorDetails = $"This action was completed on {ActionToken.UsedAt:MMMM d, yyyy 'at' h:mm tt}.";
            return;
        }

        if (ActionToken.ExpiresAt < DateTime.UtcNow)
        {
            TokenError = "Link Expired";
            TokenErrorDetails = "This action link has expired. Please contact the office for assistance.";
            return;
        }

        // Set action-specific content
        switch (ActionToken.Action)
        {
            case TokenAction.Confirm:
                ActionTitle = "Confirm Your Shift";
                ActionDescription = "Please confirm that you will be able to work the shift below.";
                ButtonText = "Confirm Shift";
                ButtonClass = "btn-success";
                break;

            case TokenAction.Decline:
                ActionTitle = "Decline Shift";
                ActionDescription = "If you cannot work this shift, please decline it so we can find coverage.";
                ButtonText = "Decline Shift";
                ButtonClass = "btn-warning";
                break;

            case TokenAction.Cancel:
                ActionTitle = "Cancel Shift";
                ActionDescription = "If you need to cancel your confirmed shift, we'll find a replacement.";
                ButtonText = "Cancel Shift";
                ButtonClass = "btn-danger";
                break;

            case TokenAction.Request:
                ActionTitle = "Request Shift";
                ActionDescription = "Request to volunteer for this open shift.";
                ButtonText = "Request Shift";
                ButtonClass = "btn-primary";
                break;
        }
    }

    public async Task<IActionResult> OnPostAsync(string token)
    {
        Token = token;
        await LoadToken();

        if (TokenError != null || ActionToken == null)
        {
            return Page();
        }

        var shift = ActionToken.Shift;

        switch (ActionToken.Action)
        {
            case TokenAction.Confirm:
                if (shift.Status == ShiftStatus.Assigned && shift.VolunteerId == ActionToken.VolunteerId)
                {
                    shift.Status = ShiftStatus.Confirmed;
                    shift.ConfirmedAt = DateTime.UtcNow;
                    CompletedTitle = "Shift Confirmed";
                    CompletedMessage = "Thank you! Your shift has been confirmed. We'll send you a reminder 24 hours before.";
                }
                else
                {
                    TokenError = "Cannot Confirm";
                    TokenErrorDetails = "This shift has been reassigned or is no longer available.";
                    return Page();
                }
                break;

            case TokenAction.Decline:
                if (shift.VolunteerId == ActionToken.VolunteerId)
                {
                    shift.Status = ShiftStatus.Open;
                    shift.VolunteerId = null;
                    shift.AssignedAt = null;
                    shift.ConfirmedAt = null;
                    CompletedTitle = "Shift Declined";
                    CompletedMessage = "The shift has been released. Thank you for letting us know.";
                }
                else
                {
                    TokenError = "Cannot Decline";
                    TokenErrorDetails = "This shift has already been reassigned.";
                    return Page();
                }
                break;

            case TokenAction.Cancel:
                if (shift.VolunteerId == ActionToken.VolunteerId)
                {
                    shift.Status = ShiftStatus.Open;
                    shift.VolunteerId = null;
                    shift.AssignedAt = null;
                    shift.ConfirmedAt = null;
                    CompletedTitle = "Shift Cancelled";
                    CompletedMessage = "Your shift has been cancelled. We'll find a replacement.";
                }
                else
                {
                    TokenError = "Cannot Cancel";
                    TokenErrorDetails = "This shift has already been reassigned.";
                    return Page();
                }
                break;

            case TokenAction.Request:
                // This would typically be handled by the request page
                CompletedTitle = "Request Submitted";
                CompletedMessage = "Your request has been submitted for review.";
                break;
        }

        // Mark token as used
        ActionToken.UsedAt = DateTime.UtcNow;

        // Log the action
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            ShiftId = shift.Id,
            VolunteerId = ActionToken.VolunteerId,
            Action = $"Token Action: {ActionToken.Action}",
            Details = $"{ActionToken.Volunteer.Name} used {ActionToken.Action} token for {shift.Date:MMM d}"
        });

        await _dbContext.SaveChangesAsync();

        ActionCompleted = true;
        return Page();
    }
}
