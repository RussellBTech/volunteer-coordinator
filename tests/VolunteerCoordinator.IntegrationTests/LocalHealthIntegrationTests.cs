using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

public sealed class LocalHealthIntegrationTests
{
    [Fact]
    public async Task DatabaseOutageRetainsLivenessAndBlocksServing()
    {
        using var factory = new CoordinatorWebFactory(
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=unavailable;Password=unavailable");

        using var client = factory.CreateClient();
        using var liveness = await client.GetAsync("/health");
        using var readiness = await client.GetAsync("/health/ready");
        using var product = await client.GetAsync("/Privacy");
        Assert.Equal(System.Net.HttpStatusCode.OK, liveness.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, product.StatusCode);
    }
}
