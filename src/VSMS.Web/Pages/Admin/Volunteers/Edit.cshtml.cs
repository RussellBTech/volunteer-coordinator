using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.Volunteers;

public class EditModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public EditModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Volunteer? Volunteer { get; set; }
    public int AssignedShiftsCount { get; set; }

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

        [Display(Name = "Active")]
        public bool IsActive { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        Volunteer = await _dbContext.Volunteers.FindAsync(Id);

        if (Volunteer == null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Name = Volunteer.Name,
            Email = Volunteer.Email,
            Phone = Volunteer.Phone,
            IsActive = Volunteer.IsActive
        };

        AssignedShiftsCount = await _dbContext.Shifts
            .CountAsync(s => s.VolunteerId == Id);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Volunteer = await _dbContext.Volunteers.FindAsync(Id);

        if (Volunteer == null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            AssignedShiftsCount = await _dbContext.Shifts.CountAsync(s => s.VolunteerId == Id);
            return Page();
        }

        // Check for duplicate email (excluding current volunteer)
        var existingEmail = await _dbContext.Volunteers
            .AnyAsync(v => v.Email.ToLower() == Input.Email.ToLower() && v.Id != Id);

        if (existingEmail)
        {
            ModelState.AddModelError("Input.Email", "A volunteer with this email already exists.");
            AssignedShiftsCount = await _dbContext.Shifts.CountAsync(s => s.VolunteerId == Id);
            return Page();
        }

        Volunteer.Name = Input.Name;
        Volunteer.Email = Input.Email;
        Volunteer.Phone = Input.Phone;
        Volunteer.IsActive = Input.IsActive;

        await _dbContext.SaveChangesAsync();

        // Log the action
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            VolunteerId = Volunteer.Id,
            Action = "Volunteer Updated",
            Details = $"Updated volunteer: {Volunteer.Name}"
        });
        await _dbContext.SaveChangesAsync();

        TempData["Success"] = $"Volunteer {Volunteer.Name} has been updated.";
        return RedirectToPage("Index");
    }
}
