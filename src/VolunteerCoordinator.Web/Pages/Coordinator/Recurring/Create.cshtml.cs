using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application.Time;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Recurring;

public sealed class CreateModel : PageModel
{
    private readonly RecurringShiftService _service;

    public CreateModel(RecurringShiftService service)
    {
        _service = service;
    }

    public GroupSettingsDto? Settings { get; private set; }
    public RecurringSeriesPreviewDto? Preview { get; private set; }

    [BindProperty, Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [BindProperty, StringLength(200)]
    public string? Location { get; set; }

    [BindProperty, StringLength(1000)]
    public string? VolunteerInstructions { get; set; }

    [BindProperty, StringLength(1000), Display(Name = "Internal coordinator notes")]
    public string? InternalCoordinatorNotes { get; set; }

    [BindProperty]
    public RecurrenceKind RecurrenceKind { get; set; } = RecurrenceKind.Weekly;

    [BindProperty, Range(1, 4)]
    public int Interval { get; set; } = 1;

    [BindProperty]
    public List<DayOfWeek> Weekdays { get; set; } = [DayOfWeek.Monday];

    [BindProperty]
    public DateOnly AnchorLocalDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [BindProperty]
    public TimeOnly LocalStartTime { get; set; } = new(9, 0);

    [BindProperty, Range(1, 10080)]
    public int DurationMinutes { get; set; } = 120;

    [BindProperty, Range(0, 2)]
    public int BackupSlotCount { get; set; }

    [BindProperty, Range(4, 26)]
    public int HorizonWeeks { get; set; } = RecurringShiftService.DefaultHorizonWeeks;

    [BindProperty]
    public AmbiguousTimeChoice AmbiguousTimeChoice { get; set; } = AmbiguousTimeChoice.FirstOccurrence;
    [BindProperty]
    public SignupPolicy SignupPolicy { get; set; } = SignupPolicy.ApprovalRequired;

    [BindProperty]
    public uint ExpectedSettingsVersion { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadSettingsAsync(cancellationToken);
        if (Settings is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        ExpectedSettingsVersion = Settings.Version;
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        await LoadSettingsAsync(cancellationToken);
        if (!ModelState.IsValid || Settings is null)
        {
            return Page();
        }

        try
        {
            Preview = await _service.PreviewRecurringSeriesAsync(BuildInput(), cancellationToken);
            return Page();
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        await LoadSettingsAsync(cancellationToken);
        if (!ModelState.IsValid || Settings is null)
        {
            return Page();
        }

        try
        {
            await _service.CreateRecurringSeriesAsync(
                BuildInput(),
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = "Recurring schedule created. Review its concrete dates before publishing.";
            return RedirectToPage("/Coordinator/Recurring/Index");
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private RecurringSeriesInput BuildInput() => new(
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
        AmbiguousTimeChoice,
        ExpectedSettingsVersion,
        SignupPolicy);

    private async Task LoadSettingsAsync(CancellationToken cancellationToken)
    {
        Settings = await _service.GetGroupSettingsAsync(cancellationToken);
        if (Settings is not null && ExpectedSettingsVersion == 0)
        {
            ExpectedSettingsVersion = Settings.Version;
        }
    }
}
