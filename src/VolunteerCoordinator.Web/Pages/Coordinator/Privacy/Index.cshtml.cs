using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Volunteers;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Privacy;

public sealed class IndexModel : PageModel
{
    private const string GenericNotFoundMessage = "No matching volunteer removal record was found.";
    private readonly VolunteerCoordinatorService _service;

    public IndexModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    [BindProperty]
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;
    [BindProperty]
    public string? NormalizedEmail { get; set; }

    [BindProperty]
    public Guid VolunteerId { get; set; }

    [BindProperty]
    public Guid SelectedShiftId { get; set; }

    public VolunteerRemovalLookup? Lookup { get; private set; }

    public string? LookupMessage { get; private set; }

    public string? BlockingMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostLookupAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        NormalizedEmail = Volunteer.NormalizeEmail(Email);
        Lookup = await _service.LookupVolunteerRemovalAsync(NormalizedEmail!, cancellationToken);
        if (Lookup is null || Lookup.Commitments.Count == 0)
        {
            Lookup = null;
            LookupMessage = GenericNotFoundMessage;
        }
        else
        {
            VolunteerId = Lookup.VolunteerId;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken cancellationToken)
    {
        if (VolunteerId == Guid.Empty || SelectedShiftId == Guid.Empty)
        {
            ModelState.AddModelError(string.Empty, "Select one recent related commitment before confirming removal.");
            return Page();
        }

        try
        {
            var result = await _service.ConfirmVolunteerRemovalAsync(
                VolunteerId,
                SelectedShiftId,
                NormalizedEmail ?? string.Empty,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            switch (result.Outcome)
            {
                case VolunteerAnonymizationOutcome.Anonymized:
                    TempData["Message"] = "Volunteer contact data removed. Non-identifying scheduling history was retained.";
                    return RedirectToPage();
                case VolunteerAnonymizationOutcome.AlreadyAnonymized:
                case VolunteerAnonymizationOutcome.NotFound:
                    LookupMessage = GenericNotFoundMessage;
                    break;
                case VolunteerAnonymizationOutcome.Blocked:
                    BlockingMessage = DescribeBlocker(result);
                    break;
                default:
                    BlockingMessage = "Volunteer contact data could not be removed. Reload and try again.";
                    break;
            }
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        return Page();
    }

    private static string DescribeBlocker(VolunteerAnonymizationResult result) => result.Blocker switch
    {
        VolunteerAnonymizationBlocker.PendingRequests =>
            $"Removal is blocked by {result.BlockingCount} pending request(s). Resolve the request before removing contact data.",
        VolunteerAnonymizationBlocker.ActiveAssignments =>
            $"Removal is blocked by {result.BlockingCount} assigned or confirmed assignment(s). End the assignment before removing contact data.",
        VolunteerAnonymizationBlocker.FutureCommitments =>
            $"Removal is blocked by {result.BlockingCount} future commitment(s). The contact record remains needed until those commitments end.",
        VolunteerAnonymizationBlocker.TooRecent =>
            "Automatic retention has not reached the 365-day anchor. Coordinator-verified removal may bypass age, but not a live dependency.",
        _ => "Removal is currently blocked by a live scheduling dependency."
    };
}
