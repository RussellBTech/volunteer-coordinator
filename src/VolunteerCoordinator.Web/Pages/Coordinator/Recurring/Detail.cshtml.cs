using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Recurring;

public sealed class DetailModel : PageModel
{
    private readonly RecurringShiftService _service;

    public DetailModel(RecurringShiftService service)
    {
        _service = service;
    }

    public RecurringSeriesDetailDto? Detail { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            Detail = await _service.GetRecurringSeriesDetailAsync(id, cancellationToken);
            return Page();
        }
        catch (DomainException)
        {
            return NotFound();
        }
    }

    public IActionResult OnPostPublish(Guid id)
    {
        return RedirectToPage("/Coordinator/Recurring/Publish", new { id });
    }

    public IActionResult OnPostRevision(Guid id)
    {
        return RedirectToPage("/Coordinator/Recurring/Revision", new { id });
    }

    public IActionResult OnPostZoneReview(Guid id)
    {
        return RedirectToPage("/Coordinator/Recurring/Zone", new { id });
    }
}
