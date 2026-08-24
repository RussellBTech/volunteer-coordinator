using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Assignments;

public sealed class AssignModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public AssignModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public CoverageDto? Coverage { get; private set; }

    public Guid? CurrentAssignmentId => Coverage?.AssignmentId;

    public IReadOnlyList<VolunteerDto> ExistingVolunteers { get; private set; } = [];

    [BindProperty, Required, StringLength(120)]
    public string VolunteerName { get; set; } = string.Empty;

    [BindProperty, Required, EmailAddress, StringLength(320)]
    public string VolunteerEmail { get; set; } = string.Empty;

    [BindProperty, Phone, StringLength(40)]
    public string? VolunteerPhone { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid slotId, CancellationToken cancellationToken) =>
        await LoadAsync(slotId, cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(Guid slotId, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(slotId, cancellationToken))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var result = await _service.AssignDirectlyAsync(slotId, VolunteerName, VolunteerEmail, VolunteerPhone, CoordinatorIdentity.GetEmail(User)!, cancellationToken);
            TempData["Message"] = "Volunteer assignment saved.";
            TempData["Warning"] = result.NotificationWarning;
            return RedirectToPage("/Coordinator/Coverage/Index");
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private async Task<bool> LoadAsync(Guid slotId, CancellationToken cancellationToken)
    {
        Coverage = (await _service.GetCoverageAsync(cancellationToken)).SingleOrDefault(x => x.SlotId == slotId);
        ExistingVolunteers = await _service.ListVolunteersAsync(cancellationToken);
        return Coverage is not null;
    }
}
