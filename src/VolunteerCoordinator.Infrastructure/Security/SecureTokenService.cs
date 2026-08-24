using System.Security.Cryptography;
using System.Text;
using VolunteerCoordinator.Application.Ports;

namespace VolunteerCoordinator.Infrastructure.Security;

public sealed class SecureTokenService : ITokenService
{
    public GeneratedToken Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new GeneratedToken(rawToken, SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    public byte[] Hash(string rawToken) => SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));

    public bool FixedTimeEquals(byte[] left, byte[] right) =>
        CryptographicOperations.FixedTimeEquals(left, right);
}
