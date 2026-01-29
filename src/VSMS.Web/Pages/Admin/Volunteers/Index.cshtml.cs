using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.Volunteers;

public class IndexModel : PageModel
{
    private readonly VsmsDbContext _dbContext;

    public IndexModel(VsmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    public List<Volunteer> Volunteers { get; set; } = new();

    public async Task OnGetAsync()
    {
        var query = _dbContext.Volunteers.AsQueryable();

        if (!string.IsNullOrEmpty(Search))
        {
            var searchLower = Search.ToLower();
            query = query.Where(v =>
                v.Name.ToLower().Contains(searchLower) ||
                v.Email.ToLower().Contains(searchLower));
        }

        query = Filter switch
        {
            "active" => query.Where(v => v.IsActive),
            "backup" => query.Where(v => v.IsBackup && v.IsActive),
            "inactive" => query.Where(v => !v.IsActive),
            _ => query
        };

        Volunteers = await query
            .OrderBy(v => v.Name)
            .ToListAsync();
    }
}
