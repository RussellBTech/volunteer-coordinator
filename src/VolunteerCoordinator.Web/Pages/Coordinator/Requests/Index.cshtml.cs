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
    private readonly RecurringCommitmentService _recurringCommitments;

    public IndexModel(
        VolunteerCoordinatorService service,
        RecurringCommitmentService recurringCommitments)
    {
        _service = service;
        _recurringCommitments = recurringCommitments;
    }

    public IReadOnlyList<CoordinatorRequestDto> Requests { get; private set; } = [];
    public IReadOnlyList<RecurringCommitmentRequestDto> RecurringRequests { get; private set; } = [];

    public bool IsFiltered { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? attention, CancellationToken cancellationToken)
    {
        IsFiltered = attention == "pending";
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        var requests = await _service.ListRequestsAsync(cancellationToken);
        Requests = IsFiltered
            ? requests.Where(x => x.Status == "Pending" && x.SlotState != "Ended" && x.SlotState != "Inactive").ToArray()
            : requests;
        RecurringRequests = await _recurringCommitments.ListRequestsAsync(cancellationToken);
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
    public async Task<IActionResult> OnPostApproveRecurringAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        try
        {
            await _recurringCommitments.ApproveAsync(
                id,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = "Recurring request approved. The volunteer receives one cadence confirmation.";
        }
        catch (DomainException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostRejectRecurringAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        try
        {
            await _recurringCommitments.RejectAsync(
                id,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = "Recurring request declined.";
        }
        catch (DomainException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage();
    }
}
