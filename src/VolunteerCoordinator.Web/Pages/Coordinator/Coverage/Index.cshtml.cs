using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Coverage;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public IndexModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public IReadOnlyList<CoverageDto> Coverage { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Coverage = await _service.GetCoverageAsync(cancellationToken);
    }
}
