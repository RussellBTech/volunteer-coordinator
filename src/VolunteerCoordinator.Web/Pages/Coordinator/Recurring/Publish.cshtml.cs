using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Recurring;

public sealed class PublishModel : PageModel
{
    private readonly RecurringShiftService _service;

    public PublishModel(RecurringShiftService service)
    {
        _service = service;
    }

    public RecurringPublicationPreviewDto? Preview { get; private set; }
    public Guid SeriesId { get; private set; }

    [BindProperty]
    public DateOnly FromLocalDate { get; set; }

    [BindProperty]
    public DateOnly ThroughLocalDate { get; set; }

    [BindProperty]
    public uint ExpectedSeriesVersion { get; set; }

    [BindProperty]
    public string ExpectedVersions { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        SeriesId = id;
        var detail = await _service.GetRecurringSeriesDetailAsync(id, cancellationToken);
        var dates = detail.Unpublished.Select(x => x.LocalDate).Concat(detail.NeedsReview.Select(x => x.LocalDate)).OrderBy(x => x).ToArray();
        FromLocalDate = dates.FirstOrDefault(DateOnly.FromDateTime(DateTime.UtcNow));
        ThroughLocalDate = dates.LastOrDefault(FromLocalDate.AddDays(7));
        ExpectedSeriesVersion = detail.ExpectedSeriesVersion;
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        SeriesId = id;
        try
        {
            Preview = await _service.PreviewPublicationAsync(id, FromLocalDate, ThroughLocalDate, cancellationToken);
            ExpectedSeriesVersion = Preview.ExpectedSeriesVersion;
            ExpectedVersions = Preview.ExpectedVersions;
            return Page();
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostConfirmAsync(Guid id, CancellationToken cancellationToken)
    {
        SeriesId = id;
        try
        {
            var result = await _service.PublishRecurringOccurrencesAsync(
                id,
                FromLocalDate,
                ThroughLocalDate,
                ExpectedSeriesVersion,
                ExpectedVersions,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = $"Published {result.ChangedCount} recurring occurrence(s).";
            return RedirectToPage("/Coordinator/Recurring/Detail", new { id });
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }
}
