using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VSMS.Web.Pages.Admin;

public class LoginModel : PageModel
{
    [TempData]
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public IActionResult OnGetGoogle()
    {
        var redirectUrl = Url.Page("/Admin/LoginCallback");
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }
}
