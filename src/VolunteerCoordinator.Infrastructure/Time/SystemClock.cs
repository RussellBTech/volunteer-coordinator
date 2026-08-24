using VolunteerCoordinator.Application.Ports;

namespace VolunteerCoordinator.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
