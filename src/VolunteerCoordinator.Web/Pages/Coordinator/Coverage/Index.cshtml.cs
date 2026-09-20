using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Coverage;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public IndexModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public IReadOnlyList<CoverageDto> Coverage { get; private set; } = [];

    public string? AppliedAttention { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? attention, CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        AppliedAttention = attention switch
        {
            "uncovered" => "uncovered",
            "unconfirmed" => "unconfirmed",
            _ => null
        };
        var allCoverage = await _service.GetCoverageAsync(cancellationToken);
        Coverage = AppliedAttention switch
        {
            "uncovered" => allCoverage.Where(x => x.State == "Uncovered").ToArray(),
            "unconfirmed" => allCoverage.Where(x => x.State == "Unconfirmed").ToArray(),
            _ => allCoverage
        };
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        if (await _service.GetGroupSettingsAsync(cancellationToken) is null)
        {
            return RedirectToPage("/Coordinator/Settings");
        }

        return RedirectToPage("/Coordinator/Assignments/Cancel", new { assignmentId });
    }
}
