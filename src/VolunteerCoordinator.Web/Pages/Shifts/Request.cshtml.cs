using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;

namespace VolunteerCoordinator.Web.Pages.Shifts;

public sealed class RequestModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public RequestModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public OpeningDto? Opening { get; private set; }

    [BindProperty]
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Phone, StringLength(40)]
    public string? Phone { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid slotId, CancellationToken cancellationToken)
    {
        return await LoadOpeningAsync(slotId, cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(Guid slotId, CancellationToken cancellationToken)
    {
        if (!await LoadOpeningAsync(slotId, cancellationToken))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var submission = await _service.SubmitRequestAsync(slotId, Name, Email, Phone, cancellationToken);
            TempData["StatusUrl"] = Url.Page(
                "/Requests/Status",
                pageHandler: null,
                values: new { token = submission.StatusToken },
                protocol: Request.Scheme);
            if (submission.NotificationWarning is not null)
            {
                TempData["Warning"] = submission.NotificationWarning;
            }

            return RedirectToPage("/Shifts/RequestComplete");
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private async Task<bool> LoadOpeningAsync(Guid slotId, CancellationToken cancellationToken)
    {
        Opening = (await _service.ListOpeningsAsync(cancellationToken)).SingleOrDefault(x => x.SlotId == slotId);
        return Opening is not null;
    }
}
