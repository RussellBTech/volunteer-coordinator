using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Audit;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public IndexModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public IReadOnlyList<AuditDto> Entries { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Entries = await _service.ListAuditAsync(200, cancellationToken);
    }
}
