using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Recurring;

public sealed class IndexModel : PageModel
{
    private readonly RecurringShiftService _service;

    public IndexModel(RecurringShiftService service)
    {
        _service = service;
    }

    public IReadOnlyList<RecurringSeriesSummaryDto> Series { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        try
        {
            Series = await _service.ListRecurringSeriesAsync(cancellationToken);
            return Page();
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }
}
