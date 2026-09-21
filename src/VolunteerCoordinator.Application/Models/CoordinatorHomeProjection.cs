using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Settings;

namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorHomeProjection(
    GroupSettings? Settings,
    bool HasPublishedShift,
    Shift? FirstUnpublishedShift,
    int PendingRequestCount,
    IReadOnlyList<CoordinatorHomeExample> PendingRequestExamples,
    int UncoveredCommitmentCount,
    IReadOnlyList<CoordinatorHomeExample> UncoveredCommitmentExamples,
    int UnconfirmedAssignmentCount,
    IReadOnlyList<CoordinatorHomeExample> UnconfirmedAssignmentExamples,
    int FailedMessageCount,
    IReadOnlyList<CoordinatorHomeExample> FailedMessageExamples,
    Shift? FirstExpiredUnpublishedShift = null,
    CoordinatorWorkSummaryDto? WorkSummary = null);
