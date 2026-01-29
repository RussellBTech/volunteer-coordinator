using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VSMS.Core.Entities;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.TimeSlots;

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

    public class InputModel
    {
        [Required]
        [StringLength(50)]
        public string Label { get; set; } = "";

        [Required]
        [Display(Name = "Start Time")]
        public TimeOnly StartTime { get; set; }

        [Required]
        [Range(30, 480)]
        [Display(Name = "Duration (minutes)")]
        public int DurationMinutes { get; set; }

        [Required]
        [Range(1, 100)]
        [Display(Name = "Sort Order")]
        public int SortOrder { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var timeSlot = await _dbContext.TimeSlots.FindAsync(Id);

        if (timeSlot == null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Label = timeSlot.Label,
            StartTime = timeSlot.StartTime,
            DurationMinutes = timeSlot.DurationMinutes,
            SortOrder = timeSlot.SortOrder,
            IsActive = timeSlot.IsActive
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var timeSlot = await _dbContext.TimeSlots.FindAsync(Id);

        if (timeSlot == null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        timeSlot.Label = Input.Label;
        timeSlot.StartTime = Input.StartTime;
        timeSlot.DurationMinutes = Input.DurationMinutes;
        timeSlot.SortOrder = Input.SortOrder;
        timeSlot.IsActive = Input.IsActive;

        await _dbContext.SaveChangesAsync();

        TempData["Success"] = $"Time slot '{timeSlot.Label}' has been updated.";
        return RedirectToPage("Index");
    }
}
