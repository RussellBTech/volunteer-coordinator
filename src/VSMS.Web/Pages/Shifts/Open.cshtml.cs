using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Shifts;

public class OpenModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public OpenModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int? Role { get; set; }

    public List<Shift> OpenShifts { get; set; } = new();

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var query = _dbContext.Shifts
            .Include(s => s.TimeSlot)
            .Where(s => s.Status == ShiftStatus.Open && s.Date >= today);

        if (Role.HasValue)
        {
            var roleFilter = (ShiftRole)Role.Value;
            query = query.Where(s => s.Role == roleFilter);
        }

        OpenShifts = await query
            .OrderBy(s => s.Date)
            .ThenBy(s => s.TimeSlot.SortOrder)
            .Take(50)
            .ToListAsync();
    }
}
