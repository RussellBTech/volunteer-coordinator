using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Schedule;

public sealed class EditModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public EditModel(VolunteerCoordinatorService service)
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

    [BindProperty]
    public uint ExpectedVersion { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var shift = (await _service.ListShiftsAsync(cancellationToken)).SingleOrDefault(x => x.Id == id);
        if (shift is null || !shift.IsActive)
        {
            return NotFound();
        }

        Title = shift.Title;
        Location = shift.Location;
        Notes = shift.Notes;
        StartsAtUtc = shift.StartsAtUtc.UtcDateTime;
        EndsAtUtc = shift.EndsAtUtc.UtcDateTime;
        BackupSlotCount = shift.Slots.Count(x => x.Kind == "Backup" && x.Status != "Inactive");
        ExpectedVersion = shift.Version;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await _service.EditShiftAsync(
                id,
                ExpectedVersion,
                Title,
                Location,
                Notes,
                AsUtc(StartsAtUtc!.Value),
                AsUtc(EndsAtUtc!.Value),
                BackupSlotCount,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = "Shift corrections saved.";
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
