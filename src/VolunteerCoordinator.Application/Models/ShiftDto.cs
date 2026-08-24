namespace VolunteerCoordinator.Application.Models;

public sealed record ShiftDto(
    Guid Id,
    string Title,
    string? Location,
    string? Notes,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    bool IsActive,
    bool IsPublished,
    uint Version,
    IReadOnlyList<SlotDto> Slots);
