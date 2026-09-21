namespace VolunteerCoordinator.Application.Models;

public sealed record AuditHistoryQueryPage(
    IReadOnlyList<AuditHistoryQueryRow> Items,
    bool HasNextPage,
    bool HasPreviousPage);
