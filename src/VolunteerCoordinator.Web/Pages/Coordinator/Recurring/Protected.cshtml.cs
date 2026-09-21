using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Recurring;

public sealed class ProtectedModel : PageModel
{
    private readonly RecurringShiftService _service;

    public ProtectedModel(RecurringShiftService service)
    {
        _service = service;
    }

    public RecurringProtectedResolutionPreviewDto? Preview { get; private set; }

    [BindProperty]
    public uint ExpectedOccurrenceVersion { get; set; }

    [BindProperty]
    public uint ExpectedSeriesVersion { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await LoadAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnPostConfirmAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _service.ResolveProtectedOccurrenceAsync(
                id,
                ExpectedOccurrenceVersion,
                ExpectedSeriesVersion,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = "The protected occurrence was resolved through the existing consequence flow and replaced with the current revision.";
            return RedirectToPage("/Coordinator/Recurring/Detail", new { id = Preview?.SeriesId });
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await LoadAsync(id, cancellationToken, true);
        }
    }

    private async Task<IActionResult> LoadAsync(Guid id, CancellationToken cancellationToken, bool preserveInput = false)
    {
        try
        {
            Preview = await _service.PreviewProtectedResolutionAsync(id, cancellationToken);
            if (!preserveInput)
            {
                ExpectedOccurrenceVersion = Preview.ExpectedOccurrenceVersion;
                ExpectedSeriesVersion = Preview.ExpectedSeriesVersion;
            }

            return Page();
        }
        catch (DomainException)
        {
            return NotFound();
        }
    }
}
