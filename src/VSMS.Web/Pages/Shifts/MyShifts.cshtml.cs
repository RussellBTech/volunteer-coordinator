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
    private const string EmailCookieName = "vsms_volunteer_email";

    public MyShiftsModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Email { get; set; }

    public Volunteer? Volunteer { get; set; }
    public List<(Shift Shift, string Role)> UpcomingShifts { get; set; } = new();
    public List<(Shift Shift, string Role)> PastShifts { get; set; } = new();
    public List<ShiftRequest> PendingRequests { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public bool ShowResults { get; set; }

    public async Task OnGetAsync()
    {
        // If no email provided, try to load from cookie
        if (string.IsNullOrWhiteSpace(Email))
        {
            Email = Request.Cookies[EmailCookieName];
        }

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

        // If volunteer found, save email to cookie for future visits
        if (Volunteer != null)
        {
            Response.Cookies.Append(EmailCookieName, Email, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(90),
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax
            });
        }

        return Page();
    }

    public IActionResult OnPostClearEmail()
    {
        Response.Cookies.Delete(EmailCookieName);
        return RedirectToPage();
    }

    private async Task LoadVolunteerShifts()
    {
        ShowResults = true;

        Volunteer = await _dbContext.Volunteers
            .FirstOrDefaultAsync(v => v.Email.ToLower() == Email!.ToLower() && v.IsActive);

        if (Volunteer == null)
        {
            ErrorMessage = "We couldn't find that email. Try the email you used when signing up, or contact the office if you need help.";
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        // Get shifts where volunteer is primary, backup1, or backup2
        var allShifts = await _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .Where(s => s.VolunteerId == Volunteer.Id
                     || s.Backup1VolunteerId == Volunteer.Id
                     || s.Backup2VolunteerId == Volunteer.Id)
            .OrderBy(s => s.Date)
            .ThenBy(s => s.TimeSlot.SortOrder)
            .ToListAsync();

        // Map shifts with their role
        var shiftsWithRole = allShifts.Select(s =>
        {
            string role = s.VolunteerId == Volunteer.Id ? "Primary"
                        : s.Backup1VolunteerId == Volunteer.Id ? "Backup 1"
                        : "Backup 2";
            return (Shift: s, Role: role);
        }).ToList();

        UpcomingShifts = shiftsWithRole
            .Where(s => s.Shift.Date >= today)
            .ToList();

        PastShifts = shiftsWithRole
            .Where(s => s.Shift.Date < today)
            .OrderByDescending(s => s.Shift.Date)
            .Take(10)
            .ToList();

        // Get pending requests
        PendingRequests = await _dbContext.ShiftRequests
            .Include(r => r.Shift)
                .ThenInclude(s => s.TimeSlot)
            .Where(r => r.VolunteerId == Volunteer.Id && r.Status == RequestStatus.Pending)
            .OrderBy(r => r.Shift.Date)
            .ToListAsync();
    }
}
