namespace VolunteerCoordinator.Application.Models;

public sealed record AuditHistoryPageDto(
    IReadOnlyList<AuditHistoryItemDto> Items,
    bool HasNextPage,
    bool HasPreviousPage,
    string GroupTimeZoneId,
    string? PositionMessage = null);
