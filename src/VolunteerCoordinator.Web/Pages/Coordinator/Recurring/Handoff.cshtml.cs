using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Recurring;

public sealed class HandoffModel : PageModel
{
    private readonly RecurringCommitmentService _service;

    public HandoffModel(RecurringCommitmentService service)
    {
        _service = service;
    }

    public RecurringCommitmentHandoffPreviewDto? Preview { get; private set; }

    [BindProperty]
    public DateOnly EffectiveLocalDate { get; set; }

    [BindProperty]
    public string ExpectedMapping { get; set; } = string.Empty;

    [BindProperty]
    public uint ExpectedCommitmentVersion { get; set; }

    [BindProperty]
    public uint ExpectedSeriesVersion { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await LoadAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnPostPreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            Preview = await _service.PreviewHandoffAsync(id, EffectiveLocalDate, cancellationToken);
            ApplyPreview();
            return Page();
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await LoadAsync(id, cancellationToken, preserveInput: true);
        }
    }

    public async Task<IActionResult> OnPostConfirmAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _service.ApplyHandoffAsync(
                id,
                EffectiveLocalDate,
                ExpectedMapping,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = "The recurring commitment was handed off to the reviewed revised occurrences.";
            return RedirectToPage("/Coordinator/Requests/Index");
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await LoadAsync(id, cancellationToken, preserveInput: true);
        }
    }

    private async Task<IActionResult> LoadAsync(
        Guid id,
        CancellationToken cancellationToken,
        bool preserveInput = false)
    {
        try
        {
            Preview = await _service.PreviewHandoffAsync(id, cancellationToken);
            if (!preserveInput)
            {
                ApplyPreview();
            }

            return Page();
        }
        catch (DomainException)
        {
            return NotFound();
        }
    }

    private void ApplyPreview()
    {
        if (Preview is null)
        {
            return;
        }

        EffectiveLocalDate = Preview.EffectiveLocalDate;
        ExpectedMapping = Preview.ExpectedMapping;
        ExpectedCommitmentVersion = Preview.ExpectedCommitmentVersion;
        ExpectedSeriesVersion = Preview.ExpectedSeriesVersion;
    }
}
