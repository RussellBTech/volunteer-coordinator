namespace VolunteerCoordinator.Application.Models;

public sealed record ActionLinkBundle(
    Guid AssignmentId,
    string? ConfirmToken,
    string? DeclineToken,
    string CancelToken);
