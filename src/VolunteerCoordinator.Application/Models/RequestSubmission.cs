namespace VolunteerCoordinator.Application.Models;

public sealed record RequestSubmission(Guid RequestId, string StatusToken, string? NotificationWarning);
