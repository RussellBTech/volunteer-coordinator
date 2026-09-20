using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Coverage;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public IndexModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public IReadOnlyList<CoverageDto> Coverage { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        Coverage = await _service.GetCoverageAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        try
        {
            await _service.CancelAssignmentAsync(
                assignmentId,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = "Assignment cancelled. The slot is now uncovered.";
        }
        catch (DomainException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage();
    }
}
