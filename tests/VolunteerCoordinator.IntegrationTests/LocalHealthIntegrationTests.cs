using System.Net;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

public sealed class LocalHealthIntegrationTests
{
    [Fact]
    public async Task ReadinessFailsWhenPostgresIsUnavailableWhileLivenessStaysHealthy()
    {
        using var factory = new CoordinatorWebFactory(
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=unavailable;Password=unavailable");
        using var client = factory.CreateClient();

        var liveness = await client.GetAsync("/health");
        var readiness = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Contains("Healthy", await liveness.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Contains("Unhealthy", await readiness.Content.ReadAsStringAsync());
    }
}
