namespace VolunteerCoordinator.Application.Models;

public sealed record LocalTimeCandidate(
    DateTime LocalTime,
    TimeSpan UtcOffset,
    DateTimeOffset UtcInstant,
    string ZoneLabel);
