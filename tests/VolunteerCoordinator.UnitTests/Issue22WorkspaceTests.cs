using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Volunteers;
using Xunit;

namespace VolunteerCoordinator.UnitTests;

public sealed class Issue22WorkspaceTests
{
    [Fact]
    public void AttentionOptionsKeepApprovedUrgencyAndPageBounds()
    {
        var options = new CoordinatorAttentionOptions();

        Assert.True(options.IsValid());
        Assert.Equal(24, options.UrgentHours);
        Assert.Equal(72, options.SoonHours);
        Assert.Equal(50, options.PageSize);
        Assert.False(new CoordinatorAttentionOptions { UrgentHours = 72, SoonHours = 24 }.IsValid());
        Assert.False(new CoordinatorAttentionOptions { PageSize = 51 }.IsValid());
    }

    [Fact]
    public void VolunteerStoresIndexedNameNormalizationAndNeutralTombstone()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var volunteer = Volunteer.Create("  Jordan Rivera ", "jordan@example.org", null, now);

        Assert.Equal("JORDAN RIVERA", volunteer.NormalizedName);
        Assert.True(volunteer.Anonymize(now.AddDays(1)));
        Assert.Equal("REMOVED VOLUNTEER", volunteer.NormalizedName);
    }

    [Fact]
    public void AuditEntryCarriesNonIdentifyingStructuredCorrelations()
    {
        var shiftId = Guid.NewGuid();
        var volunteerId = Guid.NewGuid();
        var entry = AuditEntry.Create(
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            "COORDINATOR@example.org",
            "AssignmentCreatedOrReassigned",
            "Assignment",
            Guid.NewGuid(),
            "{}",
            shiftId,
            volunteerId);

        Assert.Equal(shiftId, entry.ShiftId);
        Assert.Equal(volunteerId, entry.VolunteerId);
    }

    [Theory]
    [InlineData("pending-request", "Request to review", "Urgent")]
    [InlineData("uncovered", "Open commitment", "Soon")]
    [InlineData("handoff", "Recurring handoff", "Upcoming")]
    public void WorkRowsUsePlainStableLabels(string category, string expectedCategory, string expectedSeverity)
    {
        var item = new CoordinatorWorkItemDto(
            Guid.NewGuid(),
            category,
            1,
            expectedSeverity == "Urgent" ? 0 : expectedSeverity == "Soon" ? 1 : 2,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "Greeter",
            "Primary",
            null,
            "Open",
            "coverage",
            Guid.NewGuid());

        Assert.Equal(expectedCategory, item.CategoryLabel);
        Assert.Equal(expectedSeverity, item.SeverityLabel);
    }
}
