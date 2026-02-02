using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VSMS.Web.Pages.Admin;

public class LoginModel : PageModel
{
    private readonly IConfiguration _configuration;

    public LoginModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [TempData]
    public string? ErrorMessage { get; set; }

    public bool IsGoogleAuthConfigured { get; private set; }
    public bool IsGitHubAuthConfigured { get; private set; }

    public void OnGet()
    {
        IsGoogleAuthConfigured = !string.IsNullOrEmpty(_configuration["Google:ClientId"])
            && !string.IsNullOrEmpty(_configuration["Google:ClientSecret"]);
        IsGitHubAuthConfigured = !string.IsNullOrEmpty(_configuration["GitHub:ClientId"])
            && !string.IsNullOrEmpty(_configuration["GitHub:ClientSecret"]);
    }

    public IActionResult OnGetGoogle()
    {
        if (!IsGoogleConfigured())
        {
            ErrorMessage = "Google authentication is not configured.";
            return RedirectToPage();
        }

        var redirectUrl = Url.Page("/Admin/LoginCallback");
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    public IActionResult OnGetGitHub()
    {
        if (!IsGitHubConfigured())
        {
            ErrorMessage = "GitHub authentication is not configured.";
            return RedirectToPage();
        }

        var redirectUrl = Url.Page("/Admin/LoginCallback");
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, "GitHub");
    }

    private bool IsGoogleConfigured() => !string.IsNullOrEmpty(_configuration["Google:ClientId"])
        && !string.IsNullOrEmpty(_configuration["Google:ClientSecret"]);

    private bool IsGitHubConfigured() => !string.IsNullOrEmpty(_configuration["GitHub:ClientId"])
        && !string.IsNullOrEmpty(_configuration["GitHub:ClientSecret"]);
}
