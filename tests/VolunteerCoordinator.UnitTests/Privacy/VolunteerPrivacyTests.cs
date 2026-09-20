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
    public void StatusTokenInvalidationAndNotificationRedactionAreIdempotent()
    {
        var request = ShiftRequest.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new byte[32],
            Now.AddDays(-400),
            Now.AddDays(-399));
        var attempt = NotificationAttempt.Create(
            request.Id,
            "RequestReceived",
            "alex@example.org",
            Now.AddDays(-400));

        Assert.True(request.InvalidateStatusToken(Now));
        Assert.False(request.IsStatusTokenUsable(Now.AddDays(-1)));
        Assert.False(request.InvalidateStatusToken(Now.AddMinutes(1)));
        Assert.True(attempt.RedactDestination());
        Assert.Equal("removed", attempt.Destination);
        Assert.False(attempt.RedactDestination());
    }
}
