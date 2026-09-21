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
            return RedirectToPage("/Coordinator/Work");
        }

        return kind switch
        {
            "request" => RedirectToPage("/Coordinator/Requests/Index", new { attention = "pending" }),
            "coverage" => RedirectToPage("/Coordinator/Coverage/Index", new { attention = "uncovered" }),
            "coverage-unconfirmed" => RedirectToPage("/Coordinator/Coverage/Index", new { attention = "unconfirmed" }),
            "messages" => RedirectToPage("/Coordinator/Messages", new { attention = "message" }),
            "recurring" => RedirectToPage("/Coordinator/Recurring/Occurrence", new { id }),
            "handoff" => RedirectToPage("/Coordinator/Recurring/Handoff", new { id }),
            _ => RedirectToPage("/Coordinator/Work")
        };
    }
}
