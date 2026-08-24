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

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Shifts = await _service.ListShiftsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostPublishAsync(Guid id, uint expectedVersion, CancellationToken cancellationToken)
    {
        try
        {
            await _service.PublishShiftAsync(id, expectedVersion, CoordinatorIdentity.GetEmail(User)!, cancellationToken);
            TempData["Message"] = "Shift published. Its open slots are now public.";
        }
        catch (DomainException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeactivateAsync(Guid id, uint expectedVersion, CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeactivateShiftAsync(id, expectedVersion, CoordinatorIdentity.GetEmail(User)!, cancellationToken);
            TempData["Message"] = "Shift deactivated and removed from public discovery.";
        }
        catch (DomainException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage();
    }
}
