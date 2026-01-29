using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.TimeSlots;

public class IndexModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public IndexModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<TimeSlot> TimeSlots { get; set; } = new();

    public async Task OnGetAsync()
    {
        TimeSlots = await _dbContext.TimeSlots
            .OrderBy(t => t.SortOrder)
            .ToListAsync();
    }
}
