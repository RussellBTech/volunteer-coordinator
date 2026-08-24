using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VolunteerCoordinator.Web.Pages;

public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Shifts/Index");
}
