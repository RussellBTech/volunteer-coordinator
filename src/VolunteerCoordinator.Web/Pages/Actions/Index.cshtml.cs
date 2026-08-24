using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;

namespace VolunteerCoordinator.Web.Pages.Actions;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public IndexModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public ActionInspectionDto? Inspection { get; private set; }

    public string? Error { get; private set; }

    public string? Outcome { get; private set; }

    public async Task OnGetAsync(string token, CancellationToken cancellationToken)
    {
        Outcome = TempData["ActionOutcome"] as string;
        if (Outcome is not null)
        {
            return;
        }

        await LoadAsync(token, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.ApplyActionAsync(token, cancellationToken);
            TempData["ActionOutcome"] = $"Assignment action completed: {result.Value}.";
            if (result.NotificationWarning is not null)
            {
                TempData["Warning"] = result.NotificationWarning;
            }

            return RedirectToPage(new { token });
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(token, cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            Inspection = await _service.InspectActionAsync(token, cancellationToken);
        }
        catch (DomainException exception)
        {
            Error = exception.Message;
        }
    }
}
