using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VSMS.Core.Entities;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.TimeSlots;

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
        [StringLength(50)]
        public string Label { get; set; } = "";

        [Required]
        [Display(Name = "Start Time")]
        public TimeOnly StartTime { get; set; } = new TimeOnly(9, 0);

        [Required]
        [Range(30, 480)]
        [Display(Name = "Duration (minutes)")]
        public int DurationMinutes { get; set; } = 180;

        [Required]
        [Range(1, 100)]
        [Display(Name = "Sort Order")]
        public int SortOrder { get; set; } = 1;
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

        var timeSlot = new TimeSlot
        {
            Label = Input.Label,
            StartTime = Input.StartTime,
            DurationMinutes = Input.DurationMinutes,
            SortOrder = Input.SortOrder,
            IsActive = true
        };

        _dbContext.TimeSlots.Add(timeSlot);
        await _dbContext.SaveChangesAsync();

        TempData["Success"] = $"Time slot '{timeSlot.Label}' has been added.";
        return RedirectToPage("Index");
    }
}
