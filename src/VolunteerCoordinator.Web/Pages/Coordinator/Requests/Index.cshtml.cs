using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Requests;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public IndexModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public IReadOnlyList<CoordinatorRequestDto> Requests { get; private set; } = [];

    public bool IsFiltered { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? attention, CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        IsFiltered = string.Equals(attention, "pending", StringComparison.Ordinal);
        var requests = await _service.ListRequestsAsync(cancellationToken);
        Requests = IsFiltered
            ? requests.Where(x => x.Status == "Pending" && x.SlotState != "Ended" && x.SlotState != "Inactive").ToArray()
            : requests;
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        try
        {
            var result = await _service.ApproveRequestAsync(id, CoordinatorIdentity.GetEmail(User)!, cancellationToken);
            TempData["Message"] = "Request approved and assignment created.";
            TempData["Warning"] = result.NotificationWarning;
        }
        catch (DomainException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        try
        {
            var result = await _service.RejectRequestAsync(id, CoordinatorIdentity.GetEmail(User)!, cancellationToken);
            TempData["Message"] = "Request rejected.";
            TempData["Warning"] = result.NotificationWarning;
        }
        catch (DomainException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage();
    }
}
