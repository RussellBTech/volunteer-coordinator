namespace VolunteerCoordinator.Application.Models;

public sealed record CommandResult<T>(T Value, string? NotificationWarning = null);
