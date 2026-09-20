using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Schedule;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public IndexModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public IReadOnlyList<ShiftDto> Shifts { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        Shifts = await _service.ListShiftsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostPublishAsync(Guid id, uint expectedVersion, CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        return RedirectToPage("/Coordinator/Schedule/Publish", new { id });
    }

    public async Task<IActionResult> OnPostDeactivateAsync(Guid id, uint expectedVersion, CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        return RedirectToPage("/Coordinator/Schedule/Deactivate", new { id });
    }
}
