using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;

namespace VolunteerCoordinator.Web.Pages.Coordinator;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public IndexModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public CoordinatorHomeDto Home { get; private set; } = new(
        true,
        [],
        [],
        null,
        null,
        null);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Home = await _service.GetCoordinatorHomeAsync(cancellationToken);
    }
}
