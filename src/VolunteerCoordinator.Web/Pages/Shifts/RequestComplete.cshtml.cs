using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VolunteerCoordinator.Web.Pages.Shifts;

public sealed class RequestCompleteModel : PageModel
{
    public string? StatusUrl { get; private set; }

    public void OnGet()
    {
        StatusUrl = TempData["StatusUrl"] as string;
    }
}
