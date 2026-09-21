using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Recurring;

public sealed class ZoneModel : PageModel
{
    private readonly RecurringShiftService _service;

    public ZoneModel(RecurringShiftService service)
    {
        _service = service;
    }

    public RecurringRevisionPreviewDto? Preview { get; private set; }
    public Guid SeriesId { get; private set; }
    [BindProperty] public uint ExpectedSeriesVersion { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        SeriesId = id;
        try
        {
            Preview = await _service.PreviewZoneAdoptionAsync(id, (await _service.GetRecurringSeriesDetailAsync(id, cancellationToken)).ExpectedSeriesVersion, cancellationToken);
            ExpectedSeriesVersion = Preview.ExpectedSeriesVersion;
            return Page();
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAdoptAsync(Guid id, CancellationToken cancellationToken)
    {
        SeriesId = id;
        try
        {
            await _service.AdoptGroupZoneAsync(id, ExpectedSeriesVersion, CoordinatorIdentity.GetEmail(User)!, cancellationToken);
            TempData["Message"] = "The recurring schedule adopted the current group time zone. Protected and exception shifts stayed fixed.";
            return RedirectToPage("/Coordinator/Recurring/Detail", new { id });
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }
}
