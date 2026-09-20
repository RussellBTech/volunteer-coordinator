namespace VolunteerCoordinator.Application.Models;

public sealed record LocalScheduleInput(
    DateTime StartsAtLocal,
    DateTime EndsAtLocal,
    TimeSpan? StartsAtOffset,
    TimeSpan? EndsAtOffset,
    uint ExpectedSettingsVersion)
{
    public DateTime StartsAtUnspecified => DateTime.SpecifyKind(StartsAtLocal, DateTimeKind.Unspecified);

    public DateTime EndsAtUnspecified => DateTime.SpecifyKind(EndsAtLocal, DateTimeKind.Unspecified);
}
