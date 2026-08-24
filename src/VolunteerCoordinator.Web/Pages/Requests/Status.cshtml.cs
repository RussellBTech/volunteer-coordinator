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

    public RequestStatusDto? Status { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            Status = await _service.GetRequestStatusAsync(token, cancellationToken);
        }
        catch (DomainException exception)
        {
            Error = exception.Message;
        }
    }
}
