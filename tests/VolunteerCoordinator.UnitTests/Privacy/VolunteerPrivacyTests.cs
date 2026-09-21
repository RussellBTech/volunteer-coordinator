using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Volunteers;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Privacy;

public sealed class VolunteerPrivacyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OptionalPhoneRemainsNull()
    {
        var volunteer = Volunteer.Create(
            "Casey",
            "casey@example.org",
            null,
            Now);

        Assert.Null(volunteer.Phone);
    }

    [Fact]
    public void AnonymizeReplacesContactWithNeutralTombstoneAndCannotRestoreIt()
    {
        var volunteer = Volunteer.Create(
            "Alex Rivera",
            "alex@example.org",
            "555-0100",
            Now.AddDays(-400));

        Assert.True(volunteer.Anonymize(Now));
        Assert.Equal("Removed volunteer", volunteer.Name);
        Assert.Equal($"removed-{volunteer.Id:N}@invalid.invalid", volunteer.Email);
        Assert.Equal(volunteer.Email.ToUpperInvariant(), volunteer.NormalizedEmail);
        Assert.Null(volunteer.Phone);
        Assert.Equal(Now, volunteer.AnonymizedAtUtc);
        Assert.False(volunteer.Anonymize(Now.AddMinutes(1)));
        var exception = Assert.Throws<DomainException>(() =>
            volunteer.UpdateContact("Restored", "restored@example.org", null, Now.AddMinutes(2)));
        Assert.Equal("Removed volunteer contact data cannot be restored.", exception.Message);
    }

    [Fact]
    public void LegacyNotificationHistoryDiscardedContactValues()
    {
        var attempt = NotificationAttempt.Create(
            Guid.NewGuid(),
            "RequestReceived",
            "alex@example.org",
            Now.AddDays(-400));
        attempt.Fail(Now.AddDays(-399), "safe failure");

        var serialized = System.Text.Json.JsonSerializer.Serialize(attempt);
        Assert.DoesNotContain("alex@example.org", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Destination", serialized, StringComparison.Ordinal);
        Assert.Equal(NotificationState.Failed, attempt.State);
    }
}
