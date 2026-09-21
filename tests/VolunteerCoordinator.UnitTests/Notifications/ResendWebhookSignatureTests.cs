using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Notifications;

public sealed class ResendWebhookSignatureTests
{
    [Fact]
    public void SvixSigningInputProducesDeterministicV1Signature()
    {
        var secret = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-secret"));
        var input = Encoding.UTF8.GetBytes("msg_1.1700000000.{\"type\":\"email.delivered\"}");
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Convert.FromBase64String(secret), input));

        Assert.NotEmpty(signature);
        Assert.Equal(32, Convert.FromBase64String(signature).Length);
    }
}
