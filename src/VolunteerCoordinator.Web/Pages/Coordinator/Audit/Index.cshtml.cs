using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Audit;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;
    private readonly CoordinatorCursorProtector _cursorProtector;
    private readonly CoordinatorRouteProtector _routeProtector;

    public IndexModel(
        VolunteerCoordinatorService service,
        CoordinatorCursorProtector cursorProtector,
        CoordinatorRouteProtector routeProtector)
    {
        _service = service;
        _cursorProtector = cursorProtector;
        _routeProtector = routeProtector;
    }

    public IReadOnlyList<AuditHistoryItemDto> Entries { get; private set; } = [];

    public IReadOnlyList<AuditActorChoiceDto> Actors { get; private set; } = [];

    public IReadOnlyList<CoordinatorFilterChoiceDto> ShiftChoices { get; private set; } = [];

    public IReadOnlyList<VolunteerSearchResultDto> VolunteerChoices { get; private set; } = [];

    public string? PositionMessage { get; private set; }

    public string? NextCursor { get; private set; }

    public string? PreviousCursor { get; private set; }

    public bool HasNextPage { get; private set; }

    public bool HasPreviousPage { get; private set; }
    public string GroupTimeZoneId { get; private set; } = "Etc/UTC";
    public string ProtectFilterId(string kind, Guid id) => _routeProtector.Protect(kind, id);
    public string ProtectShiftId(Guid id) => _routeProtector.Protect("shift", id);
    public string ProtectVolunteerId(Guid id) => _routeProtector.Protect("volunteer", id);

    [BindProperty]
    [DataType(DataType.Date)]
    public string From { get; set; } = string.Empty;

    [BindProperty]
    [DataType(DataType.Date)]
    public string Through { get; set; } = string.Empty;

    [BindProperty]
    public string? Actor { get; set; }

    [BindProperty]
    public string? Category { get; set; }

    [BindProperty]
    public string? ShiftToken { get; set; }

    [BindProperty]
    public string? VolunteerToken { get; set; }

    [BindProperty]
    [StringLength(100)]
    public string ShiftSearchTerm { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(100)]
    public string VolunteerSearchTerm { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(
        string? from,
        string? through,
        string? actor,
        string? category,
        string? shift,
        string? volunteer,
        string? cursor,
        CancellationToken cancellationToken)
    {
        From = from ?? string.Empty;
        Through = through ?? string.Empty;
        Actor = NormalizeActorInput(actor);
        Category = NormalizeCategory(category);
        ShiftToken = shift;
        VolunteerToken = volunteer;
        await LoadPageAsync(cursor, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSearchShiftAsync(CancellationToken cancellationToken)
    {
        if (ShiftSearchTerm.Trim().Length < 2)
        {
            ModelState.AddModelError(nameof(ShiftSearchTerm), "Enter at least two title characters.");
        }
        else
        {
            ShiftChoices = await _service.SearchAuditShiftsAsync(ShiftSearchTerm, cancellationToken);
        }

        await LoadActorsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSelectShiftAsync(
        string? shiftToken,
        CancellationToken cancellationToken)
    {
        if (!_routeProtector.TryUnprotect(shiftToken, out var kind, out _) ||
            !string.Equals(kind, "shift", StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "Choose a schedule from the matching results.");
            await LoadActorsAsync(cancellationToken);
            return Page();
        }

        ShiftToken = shiftToken;
        await LoadActorsAsync(cancellationToken);
        return RedirectToPage(new
        {
            from = EmptyToNull(From),
            through = EmptyToNull(Through),
            actor = EmptyToNull(Actor),
            category = EmptyToNull(Category),
            shift = ShiftToken,
            volunteer = EmptyToNull(VolunteerToken)
        });
    }

    public async Task<IActionResult> OnPostSearchVolunteerAsync(CancellationToken cancellationToken)
    {
        if (VolunteerSearchTerm.Trim().Length < 3)
        {
            ModelState.AddModelError(nameof(VolunteerSearchTerm), "Enter at least 3 name or email characters.");
        }
        else
        {
            VolunteerChoices = await _service.SearchAssignableVolunteersAsync(
                VolunteerSearchTerm,
                cancellationToken);
        }

        await LoadActorsAsync(cancellationToken);
        return Page();
    }

    public IActionResult OnPostSelectVolunteer(string? volunteerToken)
    {
        if (!_routeProtector.TryUnprotect(volunteerToken, out var kind, out _) ||
            !string.Equals(kind, "volunteer", StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "Choose a volunteer from the matching results.");
            return Page();
        }

        VolunteerToken = volunteerToken;
        return RedirectToPage(new
        {
            from = EmptyToNull(From),
            through = EmptyToNull(Through),
            actor = EmptyToNull(Actor),
            category = EmptyToNull(Category),
            shift = EmptyToNull(ShiftToken),
            volunteer = VolunteerToken
        });
    }

    private async Task LoadPageAsync(string? cursorToken, CancellationToken cancellationToken)
    {
        var settings = await _service.GetGroupSettingsAsync(cancellationToken);
        var timeZoneId = settings?.TimeZoneId ?? "Etc/UTC";
        var timeZone = ResolveTimeZone(timeZoneId);
        var filter = BuildFilter(timeZone);
        await LoadActorsAsync(cancellationToken);
        if (filter is null)
        {
            return;
        }

        GroupTimeZoneId = timeZoneId;
        var fingerprint = _cursorProtector.Fingerprint(
            From,
            Through,
            Actor,
            Category,
            ShiftToken,
            VolunteerToken);
        var cursor = default(AuditHistoryCursor);
        if (!string.IsNullOrWhiteSpace(cursorToken) &&
            !_cursorProtector.TryUnprotectHistory(cursorToken, fingerprint, out cursor))
        {
            PositionMessage = "History position expired. Showing the newest matching events.";
        }

        var shiftId = TryGetFilterId(ShiftToken, "shift");
        var volunteerId = TryGetFilterId(VolunteerToken, "volunteer");
        var page = await _service.GetAuditHistoryPageAsync(
            filter with { ShiftId = shiftId, VolunteerId = volunteerId },
            cursor,
            cancellationToken);
        Entries = page.Items;
        HasNextPage = page.HasNextPage;
        HasPreviousPage = page.HasPreviousPage;
        if (Entries.Count == 0)
        {
            return;
        }

        var first = Entries[0];
        var last = Entries[^1];
        NextCursor = HasNextPage
            ? _cursorProtector.ProtectHistory(
                new AuditHistoryCursor(last.OccurredAtUtc, last.AuditId),
                fingerprint)
            : null;
        PreviousCursor = HasPreviousPage
            ? _cursorProtector.ProtectHistory(
                new AuditHistoryCursor(first.OccurredAtUtc, first.AuditId, Forward: false),
                fingerprint)
            : null;
    }
    private async Task LoadActorsAsync(CancellationToken cancellationToken)
    {
        var selectedActor = ResolveActorFilter(Actor);
        Actors = (await _service.GetAuditActorsAsync(cancellationToken))
            .Select(actor => actor with
            {
                Value = selectedActor is not null &&
                        string.Equals(actor.Value, selectedActor, StringComparison.OrdinalIgnoreCase) &&
                        _routeProtector.TryUnprotectText(Actor, "actor", out _)
                    ? Actor!
                    : _routeProtector.ProtectText("actor", actor.Value)
            })
            .ToArray();
    }

    private AuditHistoryFilter? BuildFilter(TimeZoneInfo timeZone)
    {
        DateTimeOffset? fromUtc = null;
        DateTimeOffset? throughUtc = null;
        if (!string.IsNullOrWhiteSpace(From))
        {
            if (!DateOnly.TryParseExact(From, "yyyy-MM-dd", out var fromDate))
            {
                ModelState.AddModelError(nameof(From), "Enter a valid start date.");
            }
            else
            {
                fromUtc = ConvertLocalDate(fromDate, timeZone);
            }
        }

        if (!string.IsNullOrWhiteSpace(Through))
        {
            if (!DateOnly.TryParseExact(Through, "yyyy-MM-dd", out var throughDate))
            {
                ModelState.AddModelError(nameof(Through), "Enter a valid end date.");
            }
            else
            {
                throughUtc = ConvertLocalDate(throughDate.AddDays(1), timeZone);
            }
        }

        return ModelState.IsValid
            ? new AuditHistoryFilter(
                fromUtc,
                throughUtc,
                ResolveActorFilter(Actor),
                null,
                null,
                NormalizeCategory(Category))
            : null;
    }

    private Guid? TryGetFilterId(string? token, string expectedKind)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return _routeProtector.TryUnprotect(token, out var kind, out var id) &&
               string.Equals(kind, expectedKind, StringComparison.Ordinal)
            ? id
            : null;
    }

    private static DateTimeOffset ConvertLocalDate(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone));
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private string? NormalizeActorInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return _routeProtector.TryUnprotectText(value, "actor", out _)
            ? value
            : value.Trim().ToUpperInvariant();
    }

    private string? ResolveActorFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return _routeProtector.TryUnprotectText(value, "actor", out var actor)
            ? actor.Trim().ToUpperInvariant()
            : value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeCategory(string? value) => value switch
    {
        "Schedule" or "Requests" or "Assignments" or "Volunteer actions" or "Messages" or "Privacy" or "Recurring schedules" or "Recurring commitments" or "Access" or "Other" => value,
        _ => null
    };

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
