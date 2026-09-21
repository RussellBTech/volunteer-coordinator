using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Recurring;

public sealed class OccurrenceModel : PageModel
{
    private readonly RecurringShiftService _service;

    public OccurrenceModel(RecurringShiftService service)
    {
        _service = service;
    }

    public RecurringOccurrenceReviewDto? Occurrence { get; private set; }

    [BindProperty]
    public TimeOnly ReplacementLocalStart { get; set; }

    [BindProperty, Required, StringLength(1000)]
    public string? SkipReason { get; set; }

    [BindProperty]
    public uint ExpectedVersion { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await LoadAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnPostResolveAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(id, cancellationToken, true);
            return Page();
        }

        try
        {
            await _service.ResolveOccurrenceGapAsync(
                id,
                ExpectedVersion,
                ReplacementLocalStart,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = "The gap occurrence was resolved as an exception and remains unpublished.";
            return RedirectToPage("/Coordinator/Recurring/Index");
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(id, cancellationToken, true);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSkipAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(id, cancellationToken, true);
            return Page();
        }

        try
        {
            await _service.SkipOccurrenceGapAsync(
                id,
                ExpectedVersion,
                SkipReason!,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = "The gap occurrence was skipped with an audit reason.";
            return RedirectToPage("/Coordinator/Recurring/Index");
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(id, cancellationToken, true);
            return Page();
        }
    }

    private async Task<IActionResult> LoadAsync(Guid id, CancellationToken cancellationToken, bool preserveInput = false)
    {
        try
        {
            Occurrence = await _service.GetOccurrenceReviewAsync(id, cancellationToken);
            if (!preserveInput)
            {
                ReplacementLocalStart = Occurrence.SuggestedLocalStart;
                ExpectedVersion = Occurrence.Version;
            }

            return Page();
        }
        catch (DomainException)
        {
            return NotFound();
        }
    }
}
