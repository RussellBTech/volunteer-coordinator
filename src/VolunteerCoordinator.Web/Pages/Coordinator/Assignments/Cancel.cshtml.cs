using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Presentation;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Assignments;

public sealed class CancelModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public CancelModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public CoordinatorActionPreviewDto? Preview { get; private set; }
    public PreviewRecoveryViewModel? Recovery { get; private set; }

    [BindProperty]
    public uint ExpectedShiftVersion { get; set; }
    [BindProperty]
    public uint? ExpectedSettingsVersion { get; set; }

    [BindProperty]
    public Guid? ExpectedVolunteerId { get; set; }

    [BindProperty]
    public string? ExpectedAssignmentState { get; set; }

    public ConsequencePreviewViewModel? Review { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid assignmentId, CancellationToken cancellationToken) =>
        await LoadAsync(assignmentId, cancellationToken);

    public async Task<IActionResult> OnPostAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.CancelAssignmentAsync(
                assignmentId,
                ExpectedShiftVersion,
                ExpectedVolunteerId,
                ExpectedAssignmentState,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken,
                ExpectedSettingsVersion);
            TempData["Message"] = "Assignment cancelled. The commitment is now open.";
            return RedirectToPage("/Coordinator/Coverage/Index");
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(assignmentId, cancellationToken, preserveErrors: true);
            return Page();
        }
    }

    private async Task<IActionResult> LoadAsync(
        Guid assignmentId,
        CancellationToken cancellationToken,
        bool preserveErrors = false)
    {
        try
        {
            Preview = await _service.GetCancelAssignmentPreviewAsync(assignmentId, cancellationToken);
            ExpectedShiftVersion = Preview.ExpectedShiftVersion;
            ExpectedVolunteerId = Preview.ExpectedVolunteerId;
            ExpectedAssignmentState = Preview.ExpectedAssignmentState;
            ExpectedSettingsVersion = Preview.ExpectedSettingsVersion;

            Review = new ConsequencePreviewViewModel(
                Preview,
                "Cancel this assignment?",
                "Review the volunteer, slot, local date, and result before opening the commitment again.",
                $"/Coordinator/Assignments/Cancel/{assignmentId}",
                "/Coordinator/Coverage",
                [
                    new PreviewHiddenField("assignmentId", assignmentId.ToString()),
                    new PreviewHiddenField("ExpectedShiftVersion", ExpectedShiftVersion.ToString()),
                    new PreviewHiddenField("ExpectedVolunteerId", ExpectedVolunteerId?.ToString() ?? string.Empty),
                    new PreviewHiddenField("ExpectedAssignmentState", ExpectedAssignmentState ?? string.Empty),
                    new PreviewHiddenField("ExpectedSettingsVersion", ExpectedSettingsVersion?.ToString() ?? string.Empty)
                ],
                preserveErrors
                    ? ModelState[string.Empty]?.Errors.Select(x => x.ErrorMessage).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? []
                    : []);
            return Page();
        }
        catch (DomainException)
        {
            Recovery = new PreviewRecoveryViewModel(
                VolunteerCoordinatorService.StalePreviewMessage,
                "/Coordinator/Coverage");
            return Page();
        }
    }
}
