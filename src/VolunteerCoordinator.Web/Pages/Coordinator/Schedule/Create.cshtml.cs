using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Schedule;

public sealed class CreateModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public CreateModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    [BindProperty, Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [BindProperty, StringLength(200)]
    public string? Location { get; set; }

    [BindProperty, StringLength(1000)]
    public string? Notes { get; set; }

    [BindProperty, Required]
    public DateTime? StartsAtUtc { get; set; }

    [BindProperty, Required]
    public DateTime? EndsAtUtc { get; set; }

    [BindProperty, Range(0, 2)]
    public int BackupSlotCount { get; set; }

    public void OnGet()
    {
        var nextHour = DateTime.UtcNow.AddHours(1);
        var startsAtUtc = new DateTime(nextHour.Year, nextHour.Month, nextHour.Day, nextHour.Hour, 0, 0);
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = startsAtUtc.AddHours(1);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await _service.CreateShiftAsync(
                Title,
                Location,
                Notes,
                AsUtc(StartsAtUtc!.Value),
                AsUtc(EndsAtUtc!.Value),
                BackupSlotCount,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = "Shift created. Review it before publishing.";
            return RedirectToPage("/Coordinator/Schedule/Index");
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.Zero);
}
