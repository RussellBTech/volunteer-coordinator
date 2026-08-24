using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;

namespace VolunteerCoordinator.Web.Pages.Shifts;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public IndexModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public IReadOnlyList<OpeningDto> Openings { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Openings = await _service.ListOpeningsAsync(cancellationToken);
    }
}
