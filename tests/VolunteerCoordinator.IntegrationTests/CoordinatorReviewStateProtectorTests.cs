using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using VolunteerCoordinator.Web.Security;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

public sealed class CoordinatorReviewStateProtectorTests
{
    [Fact]
    public async Task TamperingExpiryAndPurposeChangesAreRejected()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IDataProtectionProvider>();
        var protector = new CoordinatorReviewStateProtector(provider);
        var state = new CoordinatorAssignmentReviewState(
            Guid.NewGuid(),
            "assign",
            "new",
            null,
            "A volunteer",
            "a-volunteer@example.org",
            "555-0100",
            null,
            null,
            null,
            1,
            2,
            "requests:;assignments:;volunteers:",
            null,
            "A-VOLUNTEER@EXAMPLE.ORG");

        var token = protector.Protect(state);
        Assert.True(protector.TryUnprotect(token, out var recovered));
        Assert.Equal(state, recovered);

        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');
        Assert.False(protector.TryUnprotect(tampered, out _));

        var expired = protector.Protect(state, TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        Assert.False(protector.TryUnprotect(expired, out _));

        var otherPurpose = provider
            .CreateProtector("VolunteerCoordinator.Tests.OtherPurpose")
            .ToTimeLimitedDataProtector();
        var wrongPurpose = otherPurpose.Protect(
            JsonSerializer.Serialize(state),
            TimeSpan.FromMinutes(1));
        Assert.False(protector.TryUnprotect(wrongPurpose, out _));
    }
}
