namespace VolunteerCoordinator.Application.Models;

public sealed record AuditDto(
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string Action,
    string EntityKind,
    Guid EntityId,
    string DetailJson)
{
    public string Summary { get; init; } = "A recorded coordinator action occurred.";

    public string GroupTimeZoneId { get; init; } = "Etc/UTC";
    public string ActorDisplay =>
        Actor.Equals("volunteer-token", StringComparison.OrdinalIgnoreCase) ||
        Actor.Equals("volunteer-recovery", StringComparison.OrdinalIgnoreCase) ||
        Actor.StartsWith("volunteer:", StringComparison.OrdinalIgnoreCase) ||
        Actor.Equals("DIRECT CLAIM", StringComparison.OrdinalIgnoreCase)
            ? "Volunteer"
            : Actor.Equals("retention-worker", StringComparison.OrdinalIgnoreCase)
                ? "Automated privacy process"
                : Actor.Equals("recurring-worker", StringComparison.OrdinalIgnoreCase)
                    ? "Recurring schedule process"
                    : Actor;
}
