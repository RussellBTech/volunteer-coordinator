using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application.Time;
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

    public GroupSettingsDto? Settings { get; private set; }

    public LocalScheduleResolution? Resolution { get; private set; }
    public CommitmentDto? Commitment { get; private set; }

    [BindProperty, Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [BindProperty, StringLength(200)]
    public string? Location { get; set; }

    [BindProperty, StringLength(1000), Display(Name = "Internal coordinator notes")]
    public string? Notes { get; set; }

    [BindProperty, StringLength(1000)]
    public string? VolunteerInstructions { get; set; }

    [BindProperty, Required, Display(Name = "Starts at")]
    public DateTime? StartsAtLocal { get; set; }

    [BindProperty, Required, Display(Name = "Ends at")]
    public DateTime? EndsAtLocal { get; set; }

    [BindProperty]
    public string? StartsAtOffset { get; set; }

    [BindProperty]
    public string? EndsAtOffset { get; set; }

    [BindProperty, Range(0, 2)]
    public int BackupSlotCount { get; set; }

    [BindProperty]
    public uint ExpectedVersion { get; set; }

    [BindProperty]
    public uint ExpectedSettingsVersion { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Settings = await _service.GetGroupSettingsAsync(cancellationToken);
        if (Settings is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        var shift = (await _service.ListShiftsAsync(cancellationToken)).SingleOrDefault(x => x.Id == id);
        if (shift is null || !shift.IsActive)
        {
            return NotFound();
        }

        if (!TimeZoneLabels.TryGetIanaZone(Settings.TimeZoneId, out _, out var zone))
        {
            ModelState.AddModelError(string.Empty, "The configured group time zone is unavailable. Update the group time-zone setting before scheduling.");
            return Page();
        }
        Title = shift.Title;
        Location = shift.Location;
        Notes = shift.InternalCoordinatorNotes;
        Commitment = shift.Commitment;
        VolunteerInstructions = shift.Commitment.VolunteerInstructions;
        StartsAtLocal = TimeZoneInfo.ConvertTime(shift.StartsAtUtc, zone).DateTime;
        EndsAtLocal = TimeZoneInfo.ConvertTime(shift.EndsAtUtc, zone).DateTime;
        BackupSlotCount = shift.Slots.Count(x => x.Kind == "Backup" && x.Status != "Inactive");
        ExpectedVersion = shift.Version;
        ExpectedSettingsVersion = Settings.Version;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        Settings = await _service.GetGroupSettingsAsync(cancellationToken);
        if (Settings is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }
        var existingShift = (await _service.ListShiftsAsync(cancellationToken)).SingleOrDefault(x => x.Id == id);
        if (existingShift is null || !existingShift.IsActive)
        {
            return NotFound();
        }

        Commitment = existingShift.Commitment;

        if (!ModelState.IsValid || !TryReadOffsets(out var startOffset, out var endOffset))
        {
            return Page();
        }

        var input = new LocalScheduleInput(
            StartsAtLocal!.Value,
            EndsAtLocal!.Value,
            startOffset,
            endOffset,
            ExpectedSettingsVersion);
        Resolution = await _service.ResolveLocalScheduleAsync(input, cancellationToken);
        if (!Resolution.IsComplete)
        {
            AddResolutionErrors(Resolution);
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
                VolunteerInstructions,
                input,
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

    private bool TryReadOffsets(out TimeSpan? startOffset, out TimeSpan? endOffset)
    {
        startOffset = null;
        endOffset = null;
        var valid = true;
        if (!TryParseOffset(StartsAtOffset, out startOffset))
        {
            ModelState.AddModelError(nameof(StartsAtOffset), "Choose a valid time interpretation.");
            valid = false;
        }

        if (!TryParseOffset(EndsAtOffset, out endOffset))
        {
            ModelState.AddModelError(nameof(EndsAtOffset), "Choose a valid time interpretation.");
            valid = false;
        }

        return valid;
    }

    private void AddResolutionErrors(LocalScheduleResolution resolution)
    {
        foreach (var error in resolution.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (resolution.StartCandidates.Count > 0 || resolution.EndCandidates.Count > 0)
        {
            ModelState.AddModelError(string.Empty, "Choose one labelled interpretation for each repeated local time.");
        }
    }

    private static bool TryParseOffset(string? value, out TimeSpan? offset)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            offset = null;
            return true;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            offset = parsed;
            return true;
        }

        offset = null;
        return false;
    }
}
