using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Presentation;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Schedule;

public sealed class PublishModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public PublishModel(VolunteerCoordinatorService service)
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

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await LoadAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _service.PublishShiftAsync(
                id,
                ExpectedVersion,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken,
                ExpectedAffectedSet,
                ExpectedSettingsVersion);
            TempData["Message"] = "Commitments published. Volunteers can now request open commitments.";
            return Redirect("/Coordinator");
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
            Preview = await _service.GetPublishPreviewAsync(id, cancellationToken);
            ExpectedVersion = Preview.ExpectedShiftVersion;
            ExpectedAffectedSet = Preview.ExpectedAffectedSet;
            ExpectedSettingsVersion = Preview.ExpectedSettingsVersion;

            Review = new ConsequencePreviewViewModel(
                Preview,
                "Publish this schedule entry?",
                "Review the local dates, slots, and volunteer-facing details before making the openings public.",
                $"/Coordinator/Schedule/Publish/{id}",
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
        catch (DomainException exception)
        {
            Recovery = new PreviewRecoveryViewModel(
                preserveErrors || !exception.Message.Contains("ended", StringComparison.OrdinalIgnoreCase)
                    ? VolunteerCoordinatorService.StalePreviewMessage
                    : "This schedule entry has ended and cannot be published. Correct its details before reviewing publication again.",
                "/Coordinator/Schedule",
                exception.Message.Contains("ended", StringComparison.OrdinalIgnoreCase)
                    ? $"/Coordinator/Schedule/Edit/{id}"
                    : null);
            return Page();
        }
    }
}
