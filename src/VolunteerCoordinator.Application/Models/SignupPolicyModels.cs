using VolunteerCoordinator.Domain.Schedules;

namespace VolunteerCoordinator.Application.Models;

public sealed record SignupPolicyChangePreviewDto(
    Guid ShiftId,
    SignupPolicy CurrentPolicy,
    SignupPolicy ProposedPolicy,
    int PendingRequestCount,
    uint ExpectedShiftVersion,
    bool CanApply,
    string Consequence);
