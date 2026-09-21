using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Schedules;

namespace VolunteerCoordinator.Web.Pages.Recurring;

public sealed class IndexModel : PageModel
{
    private readonly RecurringCommitmentService _commitments;
    private readonly RecurringShiftService _schedules;

    public IndexModel(RecurringCommitmentService commitments, RecurringShiftService schedules)
    {
        _commitments = commitments;
        _schedules = schedules;
    }

    public RecurringSeriesDetailDto? Series { get; private set; }
    public RecurringCommitmentPreviewDto? Preview { get; private set; }

    [BindProperty]
    public SlotKind RoleKind { get; set; } = SlotKind.Primary;

    [BindProperty, Range(1, 2)]
    public int RolePosition { get; set; } = 1;

    [BindProperty, Required]
    public DateOnly EffectiveLocalDate { get; set; }

    [BindProperty, Range(4, 26)]
    public int HorizonWeeks { get; set; } = RecurringCommitmentService.DefaultHorizonWeeks;

    [BindProperty, Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [BindProperty, Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [BindProperty, Phone, StringLength(40)]
    public string? Phone { get; set; }

    [BindProperty]
    public uint ExpectedSeriesVersion { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        try
        {
            await LoadAsync(seriesId, cancellationToken);
            if (Series is null)
            {
                return NotFound();
            }

            if (EffectiveLocalDate == default)
            {
                EffectiveLocalDate = Series.Unpublished
                    .Concat(Series.Published)
                    .Select(x => x.LocalDate)
                    .DefaultIfEmpty(DateOnly.FromDateTime(DateTime.UtcNow))
                    .First();
            }

            return Page();
        }
        catch (DomainException)
        {
            return NotFound();
        }
    }

    public async Task<IActionResult> OnPostPreviewAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        await LoadAsync(seriesId, cancellationToken);
        if (!ModelState.IsValid || Series is null)
        {
            return Page();
        }

        try
        {
            Preview = await _commitments.PreviewAsync(
                seriesId,
                RoleKind,
                RolePosition,
                EffectiveLocalDate,
                HorizonWeeks,
                cancellationToken);
            return Page();
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSubmitAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        await LoadAsync(seriesId, cancellationToken);
        if (!ModelState.IsValid || Series is null)
        {
            return Page();
        }

        try
        {
            var result = await _commitments.SubmitAsync(
                seriesId,
                RoleKind,
                RolePosition,
                EffectiveLocalDate,
                HorizonWeeks,
                ExpectedSeriesVersion,
                Name,
                Email,
                Phone,
                cancellationToken);
            return RedirectToPage("/Recurring/Hub", new { token = result.StatusToken });
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private async Task LoadAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        Series = await _schedules.GetRecurringSeriesDetailAsync(seriesId, cancellationToken);
        ExpectedSeriesVersion = Series.ExpectedSeriesVersion;
    }
}
