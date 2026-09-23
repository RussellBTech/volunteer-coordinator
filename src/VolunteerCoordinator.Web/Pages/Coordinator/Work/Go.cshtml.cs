using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Work;

public sealed class GoModel : PageModel
{
    private readonly CoordinatorRouteProtector _routeProtector;

    public GoModel(CoordinatorRouteProtector routeProtector)
    {
        _routeProtector = routeProtector;
    }

    public IActionResult OnGet(string? token)
    {
        if (!_routeProtector.TryUnprotect(token, out var kind, out var id))
        {
            return RedirectToPage("/Coordinator/Work/Index");
        }

        return kind switch
        {
            "request" => Redirect($"{Url.Page("/Coordinator/Requests/Index", new { attention = "pending" })}#request-{id:N}"),
            "coverage" => RedirectToPage("/Coordinator/Assignments/Assign", new { slotId = id }),
            "coverage-unconfirmed" => RedirectToPage("/Coordinator/Coverage/Index", new { attention = "unconfirmed" }),
            "messages" => RedirectToPage("/Coordinator/Messages", new { attention = "message" }),
            "recurring" => RedirectToPage("/Coordinator/Recurring/Occurrence", new { id }),
            "recurring-zone" => RedirectToPage("/Coordinator/Recurring/Zone", new { id }),
            "handoff" => RedirectToPage("/Coordinator/Recurring/Handoff", new { id }),
            _ => RedirectToPage("/Coordinator/Work/Index")
        };
    }
}
