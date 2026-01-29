using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Shifts;

public class MyShiftsModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public MyShiftsModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Email { get; set; }

    public Volunteer? Volunteer { get; set; }
    public List<Shift> UpcomingShifts { get; set; } = new();
    public List<Shift> PastShifts { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public bool ShowResults { get; set; }

    public async Task OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            return;
        }

        await LoadVolunteerShifts();
    }

    public async Task<IActionResult> OnPostAsync(string email)
    {
        Email = email;

        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Please enter your email address.";
            return Page();
        }

        await LoadVolunteerShifts();
        return Page();
    }

    private async Task LoadVolunteerShifts()
    {
        ShowResults = true;

        Volunteer = await _dbContext.Volunteers
            .FirstOrDefaultAsync(v => v.Email.ToLower() == Email!.ToLower() && v.IsActive);

        if (Volunteer == null)
        {
            ErrorMessage = "No active volunteer found with that email address.";
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        var allShifts = await _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .Where(s => s.VolunteerId == Volunteer.Id)
            .OrderBy(s => s.Date)
            .ThenBy(s => s.TimeSlot.SortOrder)
            .ToListAsync();

        UpcomingShifts = allShifts
            .Where(s => s.Date >= today)
            .ToList();

        PastShifts = allShifts
            .Where(s => s.Date < today)
            .OrderByDescending(s => s.Date)
            .Take(10)
            .ToList();
    }
}
