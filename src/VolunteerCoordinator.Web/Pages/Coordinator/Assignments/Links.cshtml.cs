using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Assignments;

public sealed class LinksModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public LinksModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public IReadOnlyDictionary<string, string> Links { get; private set; } = new Dictionary<string, string>();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        try
        {
            var generated = await _service.GenerateActionLinksAsync(assignmentId, CoordinatorIdentity.GetEmail(User)!, cancellationToken);
            var links = new Dictionary<string, string>();
            if (generated.ConfirmToken is not null)
            {
                links["Confirm"] = ActionUrl(generated.ConfirmToken);
            }
            if (generated.DeclineToken is not null)
            {
                links["Decline"] = ActionUrl(generated.DeclineToken);
            }
            links["Cancel"] = ActionUrl(generated.CancelToken);
            Links = links;
            return Page();
        }
        catch (DomainException exception)
        {
            TempData["Warning"] = exception.Message;
            return RedirectToPage("/Coordinator/Coverage/Index");
        }
    }

    private string ActionUrl(string token) => Url.Page(
        "/Actions/Index",
        pageHandler: null,
        values: new { token },
        protocol: Request.Scheme) ?? string.Empty;
}
