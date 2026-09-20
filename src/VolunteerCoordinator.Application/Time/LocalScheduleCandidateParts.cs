namespace VolunteerCoordinator.Application.Time;

public sealed record LocalScheduleCandidateParts(
    DateTime LocalTime,
    TimeSpan UtcOffset,
    DateTimeOffset UtcInstant);
