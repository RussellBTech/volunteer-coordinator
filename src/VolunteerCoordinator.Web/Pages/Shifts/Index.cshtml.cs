using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;

namespace VolunteerCoordinator.Web.Pages.Shifts;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;
    private readonly RecurringShiftService _recurringService;

    public IndexModel(
        VolunteerCoordinatorService service,
        RecurringShiftService recurringService)
    {
        _service = service;
        _recurringService = recurringService;
    }

    public IReadOnlyList<OpeningDto> Openings { get; private set; } = [];
    public IReadOnlyList<RecurringSeriesSummaryDto> RecurringSeries { get; private set; } = [];

    public bool IsTimeZoneConfigured { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IsTimeZoneConfigured = await _service.GetGroupSettingsAsync(cancellationToken) is not null;
        if (IsTimeZoneConfigured)
        {
            Openings = await _service.ListOpeningsAsync(cancellationToken);
            RecurringSeries = await _recurringService.ListRecurringSeriesAsync(cancellationToken);
        }
    }
}
