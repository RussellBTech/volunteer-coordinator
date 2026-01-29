using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.Volunteers;

public class CreateModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public CreateModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = "";

        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Phone]
        public string? Phone { get; set; }

        [Display(Name = "Backup Volunteer")]
        public bool IsBackup { get; set; }
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Check for duplicate email
        var existingEmail = await _dbContext.Volunteers
            .AnyAsync(v => v.Email.ToLower() == Input.Email.ToLower());

        if (existingEmail)
        {
            ModelState.AddModelError("Input.Email", "A volunteer with this email already exists.");
            return Page();
        }

        var volunteer = new Volunteer
        {
            Name = Input.Name,
            Email = Input.Email,
            Phone = Input.Phone,
            IsBackup = Input.IsBackup,
            IsActive = true
        };

        _dbContext.Volunteers.Add(volunteer);
        await _dbContext.SaveChangesAsync();

        // Log the action
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            VolunteerId = volunteer.Id,
            Action = "Volunteer Created",
            Details = $"Added volunteer: {volunteer.Name} ({volunteer.Email})"
        });
        await _dbContext.SaveChangesAsync();

        TempData["Success"] = $"Volunteer {volunteer.Name} has been added.";
        return RedirectToPage("Index");
    }
}
