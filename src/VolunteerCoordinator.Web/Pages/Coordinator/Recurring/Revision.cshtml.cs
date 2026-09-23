using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Recurring;

public sealed class RevisionModel : PageModel
{
    private readonly RecurringShiftService _service;

    public RevisionModel(RecurringShiftService service)
    {
        _service = service;
    }

    public RecurringRevisionPreviewDto? Preview { get; private set; }
    public Guid SeriesId { get; private set; }

    [BindProperty]
    public DateOnly EffectiveLocalDate { get; set; }

    [BindProperty, Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [BindProperty, StringLength(200)]
    public string? Location { get; set; }

    [BindProperty, StringLength(1000)]
    public string? VolunteerInstructions { get; set; }

    [BindProperty, StringLength(1000)]
    public string? InternalCoordinatorNotes { get; set; }

    [BindProperty]
    public RecurrenceKind RecurrenceKind { get; set; }

    [BindProperty, Range(1, 4)]
    public int Interval { get; set; }

    [BindProperty]
    public List<DayOfWeek> Weekdays { get; set; } = [];

    [BindProperty]
    public DateOnly AnchorLocalDate { get; set; }

    [BindProperty]
    public TimeOnly LocalStartTime { get; set; }

    [BindProperty, Range(1, 10080)]
    public int DurationMinutes { get; set; }

    [BindProperty, Range(0, 2)]
    public int BackupSlotCount { get; set; }

    [BindProperty, Range(4, 26)]
    public int HorizonWeeks { get; set; }

    [BindProperty]
    public string TimeZoneId { get; set; } = string.Empty;

    [BindProperty]
    public AmbiguousTimeChoice AmbiguousTimeChoice { get; set; }
    [BindProperty]
    public SignupPolicy SignupPolicy { get; set; } = SignupPolicy.ApprovalRequired;

    [BindProperty]
    public uint ExpectedSeriesVersion { get; set; }

    [BindProperty]
    public string? ExpectedClassification { get; set; }

    [BindProperty]
    public uint ExpectedSettingsVersion { get; set; }
    [BindProperty]
    public bool ConfirmPolicyChange { get; set; }

    [BindProperty]
    public SignupPolicy? ExpectedCurrentPolicy { get; set; }


    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        SeriesId = id;
        try
        {
            Apply(await _service.GetCurrentRevisionInputAsync(id, cancellationToken));
            EffectiveLocalDate = DateOnly.FromDateTime(DateTime.UtcNow);
            return Page();
        }
        catch (DomainException)
        {
            return NotFound();
        }
    }

    public async Task<IActionResult> OnPostPreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        SeriesId = id;
        try
        {
            Preview = await _service.PreviewRevisionAsync(id, BuildInput(), cancellationToken);
            ExpectedSeriesVersion = Preview.ExpectedSeriesVersion;
            ExpectedCurrentPolicy = Preview.CurrentPolicy;
            ConfirmPolicyChange = false;

            ExpectedClassification = Preview.ExpectedClassification;
            ModelState.Remove(nameof(ExpectedSeriesVersion));
            ModelState.Remove(nameof(ExpectedCurrentPolicy));
            ModelState.Remove(nameof(ExpectedClassification));
            ModelState.Remove(nameof(ConfirmPolicyChange));
            return Page();
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostApplyAsync(Guid id, CancellationToken cancellationToken)
    {
        SeriesId = id;
        try
        {
            await _service.ApplyRevisionAsync(id, BuildInput(), CoordinatorIdentity.GetEmail(User)!, cancellationToken);
            TempData["Message"] = "A new effective revision was saved. Protected and manually changed occurrences remain independent.";
            return RedirectToPage("/Coordinator/Recurring/Detail", new { id });
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private RecurringRevisionInput BuildInput() => new(
        EffectiveLocalDate,
        Title,
        Location,
        VolunteerInstructions,
        InternalCoordinatorNotes,
        RecurrenceKind,
        Interval,
        Weekdays,
        AnchorLocalDate,
        LocalStartTime,
        DurationMinutes,
        BackupSlotCount,
        HorizonWeeks,
        TimeZoneId,
        AmbiguousTimeChoice,
        ExpectedSeriesVersion,
        ExpectedClassification,
        ExpectedSettingsVersion,
        SignupPolicy,
        ConfirmPolicyChange,
        ExpectedCurrentPolicy);

    private void Apply(RecurringRevisionInput input)
    {
        EffectiveLocalDate = input.EffectiveLocalDate;
        Title = input.Title;
        Location = input.Location;
        VolunteerInstructions = input.VolunteerInstructions;
        InternalCoordinatorNotes = input.InternalCoordinatorNotes;
        RecurrenceKind = input.RecurrenceKind;
        Interval = input.Interval;
        Weekdays = input.Weekdays.ToList();
        AnchorLocalDate = input.AnchorLocalDate;
        LocalStartTime = input.LocalStartTime;
        DurationMinutes = input.DurationMinutes;
        BackupSlotCount = input.BackupSlotCount;
        HorizonWeeks = input.HorizonWeeks;
        TimeZoneId = input.TimeZoneId;
        AmbiguousTimeChoice = input.AmbiguousTimeChoice;
        SignupPolicy = input.SignupPolicy;
        ExpectedSeriesVersion = input.ExpectedSeriesVersion;
        ExpectedClassification = input.ExpectedClassification;
        ExpectedSettingsVersion = input.ExpectedSettingsVersion;
        ConfirmPolicyChange = input.ConfirmPolicyChange;
        ExpectedCurrentPolicy = input.ExpectedCurrentPolicy;
    }
}
