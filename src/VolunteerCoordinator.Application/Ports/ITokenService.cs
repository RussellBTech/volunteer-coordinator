namespace VolunteerCoordinator.Application.Ports;

public interface ITokenService
{
    GeneratedToken Generate();

    byte[] Hash(string rawToken);

    bool FixedTimeEquals(byte[] left, byte[] right);
}
