using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin;

public class LoginCallbackModel : PageModel
{
    private readonly VsmsDbContext _dbContext;
    private readonly ILogger<LoginCallbackModel> _logger;

    public LoginCallbackModel(VsmsDbContext dbContext, ILogger<LoginCallbackModel> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Authentication failed");
            TempData["ErrorMessage"] = "Authentication failed. Please try again.";
            return RedirectToPage("/Admin/Login");
        }

        var email = result.Principal?.FindFirstValue(ClaimTypes.Email);
        var googleId = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = result.Principal?.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(googleId))
        {
            _logger.LogWarning("Missing email or Google ID in claims");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["ErrorMessage"] = "Could not retrieve your Google account information.";
            return RedirectToPage("/Admin/Login");
        }

        // Check if user is in admin whitelist
        var adminUser = await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.Email.ToLower() == email.ToLower());

        if (adminUser == null)
        {
            _logger.LogWarning("Unauthorized login attempt from {Email}", email);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["ErrorMessage"] = "You are not authorized to access the admin area. Contact the administrator if you believe this is an error.";
            return RedirectToPage("/Admin/Login");
        }

        // Update Google ID if it changed (shouldn't happen often)
        if (adminUser.GoogleId != googleId)
        {
            adminUser.GoogleId = googleId;
            await _dbContext.SaveChangesAsync();
        }

        // Update name if changed
        if (!string.IsNullOrEmpty(name) && adminUser.Name != name)
        {
            adminUser.Name = name;
            await _dbContext.SaveChangesAsync();
        }

        _logger.LogInformation("Admin {Email} logged in successfully", email);

        return RedirectToPage("/Admin/Index");
    }
}
