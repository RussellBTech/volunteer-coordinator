using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;

namespace VolunteerCoordinator.Application.Models;

public sealed record VolunteerRemovalLookupProjection(
    Guid VolunteerId,
    IReadOnlyList<VolunteerRemovalCommitmentProjection> Commitments);

public sealed record VolunteerRemovalCommitmentProjection(
    Guid ShiftId,
    Guid SlotId,
    string ShiftTitle,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string? Location,
    string? VolunteerInstructions,
    SlotKind SlotKind,
    int SlotPosition,
    RequestStatus? RequestStatus,
    AssignmentStatus? AssignmentStatus);

public sealed record VolunteerRemovalLookup(
    Guid VolunteerId,
    IReadOnlyList<VolunteerRemovalCommitment> Commitments);

public sealed record VolunteerRemovalCommitment(
    Guid ShiftId,
    CommitmentDto Commitment,
    string Status);
