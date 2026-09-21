namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorWorkItemDto(
    Guid StableId,
    string Category,
    int CategoryRank,
    int SeverityRank,
    DateTimeOffset DueAtUtc,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    Guid? ShiftId,
    Guid? SlotId,
    Guid? VolunteerId,
    Guid? SeriesId,
    Guid? CommitmentId,
    string Title,
    string Context,
    string? PersonName,
    string State,
    string RouteKind,
    Guid RouteId)
{
    public string CategoryLabel => Category switch
    {
        "pending-request" => "Request to review",
        "uncovered" => "Open commitment",
        "unconfirmed" => "Waiting for confirmation",
        "message" => "Message needs follow-up",
        "withdrawal" => "Recurring withdrawal risk",
        "recurrence-review" => "Recurring schedule review",
        "zone-review" => "Time-zone review",
        "handoff" => "Recurring handoff",
        _ => "Coordinator work"
    };

    public string SeverityLabel => SeverityRank switch
    {
        0 => "Urgent",
        1 => "Soon",
        _ => "Upcoming"
    };
}
