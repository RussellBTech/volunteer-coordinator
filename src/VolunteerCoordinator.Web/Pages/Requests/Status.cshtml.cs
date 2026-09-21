using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;

namespace VolunteerCoordinator.Web.Pages.Requests;

public sealed class StatusModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public StatusModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public CommitmentHubDto? Hub { get; private set; }

    public RequestStatusDto? Status => Hub is null
        ? null
        : new RequestStatusDto(
            Hub.RequestId,
            Hub.VolunteerName,
            Hub.Commitment,
            Hub.RequestStatus,
            Hub.AssignmentStatus);

    public string? Error { get; private set; }

    public async Task OnGetAsync(string token, CancellationToken cancellationToken)
    {
        SetPrivateHeaders();
        await LoadAsync(token, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(
        string token,
        string action,
        CancellationToken cancellationToken)
    {
        SetPrivateHeaders();
        try
        {
            await _service.ApplyHubActionAsync(token, action, cancellationToken);
            return RedirectToPage(new { token });
        }
        catch (DomainException)
        {
            Error = "This commitment link is invalid, expired, or the action is no longer available.";
            return Page();
        }
    }

    private async Task LoadAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            Hub = await _service.InspectCommitmentHubAsync(token, cancellationToken);
        }
        catch (DomainException exception)
        {
            Error = string.Equals(
                exception.Message,
                VolunteerCoordinatorService.CommitmentUnavailableMessage,
                StringComparison.Ordinal)
                ? exception.Message
                : "This commitment link is invalid or has expired.";
        }
    }

    private void SetPrivateHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
