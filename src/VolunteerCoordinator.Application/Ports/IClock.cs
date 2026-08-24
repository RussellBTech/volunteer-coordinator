namespace VolunteerCoordinator.Application.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
