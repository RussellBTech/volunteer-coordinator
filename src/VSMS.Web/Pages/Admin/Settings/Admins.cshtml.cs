using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.Settings;

public class AdminsModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public AdminsModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<AdminUser> Admins { get; set; } = new();
    public string? CurrentUserEmail { get; set; }

    public async Task OnGetAsync()
    {
        Admins = await _dbContext.AdminUsers
            .OrderBy(a => a.Name)
            .ToListAsync();

        CurrentUserEmail = User.FindFirstValue(ClaimTypes.Email);
    }

    public async Task<IActionResult> OnPostAddAsync(string email, string name)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(name))
        {
            TempData["Error"] = "Email and name are required.";
            return RedirectToPage();
        }

        var exists = await _dbContext.AdminUsers
            .AnyAsync(a => a.Email.ToLower() == email.ToLower());

        if (exists)
        {
            TempData["Error"] = "An admin with this email already exists.";
            return RedirectToPage();
        }

        var admin = new AdminUser
        {
            Email = email,
            Name = name,
            GoogleId = "pending-first-login"
        };

        _dbContext.AdminUsers.Add(admin);
        await _dbContext.SaveChangesAsync();

        TempData["Success"] = $"Admin {name} has been added. They can now log in with Google.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int adminId)
    {
        var admin = await _dbContext.AdminUsers.FindAsync(adminId);

        if (admin == null)
        {
            return NotFound();
        }

        var currentEmail = User.FindFirstValue(ClaimTypes.Email);
        if (admin.Email.Equals(currentEmail, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "You cannot remove yourself.";
            return RedirectToPage();
        }

        _dbContext.AdminUsers.Remove(admin);
        await _dbContext.SaveChangesAsync();

        TempData["Success"] = $"Admin {admin.Name} has been removed.";
        return RedirectToPage();
    }
}
