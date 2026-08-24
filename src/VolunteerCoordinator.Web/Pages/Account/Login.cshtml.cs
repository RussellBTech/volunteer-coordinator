using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VolunteerCoordinator.Web.Pages.Account;

public sealed class LoginModel : PageModel
{
    private readonly IConfiguration _configuration;

    public LoginModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool OidcConfigured => !string.IsNullOrWhiteSpace(_configuration["Oidc:Authority"])
        && !string.IsNullOrWhiteSpace(_configuration["Oidc:ClientId"]);

    public IActionResult OnGetOidc(string? returnUrl)
    {
        if (!OidcConfigured)
        {
            return RedirectToPage();
        }

        var redirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/Coordinator/Schedule";
        return Challenge(new AuthenticationProperties { RedirectUri = redirectUri }, "oidc");
    }
}
