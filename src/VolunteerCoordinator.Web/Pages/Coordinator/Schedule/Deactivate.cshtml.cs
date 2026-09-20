using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Presentation;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Schedule;

public sealed class DeactivateModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public DeactivateModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public CoordinatorActionPreviewDto? Preview { get; private set; }
    public PreviewRecoveryViewModel? Recovery { get; private set; }

    [BindProperty]
    public uint ExpectedVersion { get; set; }
    [BindProperty]
    public uint? ExpectedSettingsVersion { get; set; }

    [BindProperty]
    public string? ExpectedAffectedSet { get; set; }
    public ConsequencePreviewViewModel? Review { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        await LoadAsync(id, cancellationToken);

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeactivateShiftAsync(
                id,
                ExpectedVersion,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken,
                ExpectedAffectedSet,
                ExpectedSettingsVersion);
            TempData["Message"] = "Schedule entry deactivated. Requests, assignments, and action links were resolved.";
            return RedirectToPage("/Coordinator/Schedule/Index");
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(id, cancellationToken, preserveErrors: true);
            return Page();
        }
    }

    private async Task<IActionResult> LoadAsync(
        Guid id,
        CancellationToken cancellationToken,
        bool preserveErrors = false)
    {
        try
        {
            Preview = await _service.GetDeactivatePreviewAsync(id, cancellationToken);
            ExpectedVersion = Preview.ExpectedShiftVersion;
            ExpectedAffectedSet = Preview.ExpectedAffectedSet;
            ExpectedSettingsVersion = Preview.ExpectedSettingsVersion;

            Review = new ConsequencePreviewViewModel(
                Preview,
                "Deactivate this schedule entry?",
                "This review lists the people and commitments affected. Nothing changes until you confirm.",
                $"/Coordinator/Schedule/Deactivate/{id}",
                "/Coordinator/Schedule",
                [
                    new PreviewHiddenField("id", id.ToString()),
                    new PreviewHiddenField("ExpectedVersion", ExpectedVersion.ToString()),
                    new PreviewHiddenField("ExpectedAffectedSet", ExpectedAffectedSet ?? string.Empty),
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
                "/Coordinator/Schedule");
            return Page();
        }
    }
}
