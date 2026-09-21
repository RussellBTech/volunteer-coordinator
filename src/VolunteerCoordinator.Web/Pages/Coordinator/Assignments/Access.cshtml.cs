using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Assignments;

public sealed class AccessModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public AccessModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public CommitmentDto? Commitment { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        try
        {
            Commitment = await _service.GetAssignmentCommitmentAsync(assignmentId, cancellationToken);
        }
        catch (DomainException exception)
        {
            Error = exception.Message;
        }
    }

    public async Task<IActionResult> OnPostAsync(
        Guid assignmentId,
        bool revokeNow,
        CancellationToken cancellationToken)
    {
        try
        {
            await _service.RequestAccessReissueAsync(
                assignmentId,
                revokeNow,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Success"] = revokeNow
                ? "Current access was revoked and replacement delivery was queued."
                : "Replacement access delivery was queued; current access remains valid until redemption.";
        }
        catch (DomainException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage("/Coordinator/Coverage/Index");
    }
}
