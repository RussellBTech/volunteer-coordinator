using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Work;

public sealed class IndexModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;
    private readonly CoordinatorCursorProtector _cursorProtector;
    private readonly CoordinatorRouteProtector _routeProtector;
    private readonly CoordinatorAttentionOptions _options;

    public IndexModel(
        VolunteerCoordinatorService service,
        CoordinatorCursorProtector cursorProtector,
        CoordinatorRouteProtector routeProtector,
        IOptions<CoordinatorAttentionOptions> options)
    {
        _service = service;
        _cursorProtector = cursorProtector;
        _routeProtector = routeProtector;
        _options = options.Value;
    }

    public IReadOnlyList<CoordinatorWorkItemViewModel> Items { get; private set; } = [];

    public string? AppliedCategory { get; private set; }

    public int? AppliedSeverityRank { get; private set; }

    public bool HasNextPage { get; private set; }

    public bool HasPreviousPage { get; private set; }

    public string? NextCursor { get; private set; }

    public string? PreviousCursor { get; private set; }

    public string? PositionMessage { get; private set; }

    public async Task OnGetAsync(
        string? category,
        int? severity,
        string? cursor,
        CancellationToken cancellationToken)
    {
        AppliedCategory = NormalizeCategory(category);
        AppliedSeverityRank = severity is >= 0 and <= 2 ? severity : null;
        var fingerprint = _cursorProtector.Fingerprint(
            AppliedCategory,
            AppliedSeverityRank?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var cursorValid = string.IsNullOrWhiteSpace(cursor) ||
                          _cursorProtector.TryUnprotectWork(cursor, fingerprint, out _);
        CoordinatorWorkCursor? boundary = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!cursorValid || !_cursorProtector.TryUnprotectWork(cursor, fingerprint, out boundary))
            {
                PositionMessage = "Work position expired. Showing the highest-priority matching work.";
            }
        }

        var page = await _service.GetCoordinatorWorkPageAsync(
            new CoordinatorWorkFilter(AppliedCategory, AppliedSeverityRank),
            boundary,
            _options,
            cancellationToken);
        Items = page.Items
            .Select(item => new CoordinatorWorkItemViewModel(
                item,
                _routeProtector.Protect(item.RouteKind, item.RouteId)))
            .ToArray();
        HasNextPage = page.HasNextPage;
        HasPreviousPage = page.HasPreviousPage;
        PositionMessage ??= page.PositionMessage;
        if (Items.Count > 0)
        {
            var first = Items[0].Item;
            var last = Items[^1].Item;
            NextCursor = HasNextPage
                ? _cursorProtector.ProtectWork(
                    new CoordinatorWorkCursor(last.SeverityRank, last.DueAtUtc, last.CategoryRank, last.StableId),
                    fingerprint)
                : null;
            PreviousCursor = HasPreviousPage
                ? _cursorProtector.ProtectWork(
                    new CoordinatorWorkCursor(first.SeverityRank, first.DueAtUtc, first.CategoryRank, first.StableId, Forward: false),
                    fingerprint)
                : null;
        }
    }

    private static string? NormalizeCategory(string? value) => value switch
    {
        "pending-request" or "uncovered" or "unconfirmed" or "message" or "withdrawal" or "recurrence-review" or "zone-review" or "handoff" => value,
        _ => null
    };

    public sealed record CoordinatorWorkItemViewModel(
        CoordinatorWorkItemDto Item,
        string ProtectedRoute);
}
