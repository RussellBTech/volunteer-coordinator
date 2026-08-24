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
        var links = new Dictionary<string, string>();
        foreach (var action in new[] { "Confirm", "Decline", "Cancel" })
        {
            if (TempData[$"ActionLink:{action}"] is string url)
            {
                links[action] = url;
            }
        }

        Links = links;
    }

    public async Task<IActionResult> OnPostAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        try
        {
            var links = await _service.GenerateActionLinksAsync(assignmentId, CoordinatorIdentity.GetEmail(User)!, cancellationToken);
            TempData["ActionLink:Confirm"] = ActionUrl(links.ConfirmToken);
            TempData["ActionLink:Decline"] = ActionUrl(links.DeclineToken);
            TempData["ActionLink:Cancel"] = ActionUrl(links.CancelToken);
            return RedirectToPage(new { assignmentId });
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
