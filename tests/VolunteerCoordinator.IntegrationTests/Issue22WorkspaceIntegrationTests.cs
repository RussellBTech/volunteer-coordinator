using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Settings;
using VolunteerCoordinator.Domain.Volunteers;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue22WorkspaceIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Coordinator = "coordinator@example.org";
    private readonly PostgreSqlFixture _fixture;

    public Issue22WorkspaceIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task VolunteerSearchIsPrivateBoundedAndOneCommand()
    {
        await _fixture.ResetAsync();
        await using (var seed = _fixture.CreateContext())
        {
            seed.GroupSettings.Add(VolunteerCoordinator.Domain.Settings.GroupSettings.Create("Etc/UTC"));
            for (var index = 0; index < 12; index++)
            {
                seed.Volunteers.Add(Volunteer.Create(
                    $"Alice Volunteer {index:00}",
                    $"alice-{index:00}@example.org",
                    $"555-{index:0000}",
                    Now));
            }

            var removed = Volunteer.Create(
                "Alice Removed",
                "alice-removed@example.org",
                "555-9999",
                Now);
            removed.Anonymize(Now.AddDays(1));
            seed.Volunteers.Add(removed);
            await seed.SaveChangesAsync();
        }

        var capture = new SqlCommandCaptureInterceptor();
        await using var context = _fixture.CreateContext(null, capture);
        var service = ScheduleTestHelpers.CreateService(
            context,
            new ScheduleTestHelpers.FixedClock(Now));
        var results = await service.SearchAssignableVolunteersAsync(" alice ", default);

        Assert.Equal(10, results.Count);
        Assert.All(results, result =>
        {
            Assert.StartsWith("Alice Volunteer", result.Name, StringComparison.Ordinal);
            Assert.Contains("@example.org", result.Email, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(results, result => result.Email.Contains("removed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, capture.Count);

        await Assert.ThrowsAsync<VolunteerCoordinator.Domain.DomainException>(() =>
            service.SearchAssignableVolunteersAsync("Al", default));
        await Assert.ThrowsAsync<VolunteerCoordinator.Domain.DomainException>(() =>
            service.SearchAssignableVolunteersAsync(new string('A', 101), default));
    }
    [Fact]
    public async Task WorkOrdersCategoriesBeforeDueTimeAndDeduplicatesMessageTransition()
    {
        await _fixture.ResetAsync();
        await using (var seed = _fixture.CreateContext())
        {
            seed.GroupSettings.Add(GroupSettings.Create("Etc/UTC"));

            var uncovered = CreatePublishedShift("Uncovered", Now.AddHours(4));
            var message = CreatePublishedShift("Message", Now.AddHours(6));
            var unconfirmed = CreatePublishedShift("Unconfirmed", Now.AddHours(8));
            var pending = CreatePublishedShift("Pending", Now.AddHours(10));
            var messageVolunteer = Volunteer.Create("Message volunteer", "message@example.org", null, Now);
            var unconfirmedVolunteer = Volunteer.Create("Unconfirmed volunteer", "unconfirmed@example.org", null, Now);
            var pendingVolunteer = Volunteer.Create("Pending volunteer", "pending@example.org", null, Now);
            seed.Shifts.AddRange(uncovered, message, unconfirmed, pending);
            seed.Volunteers.AddRange(messageVolunteer, unconfirmedVolunteer, pendingVolunteer);

            var messageAssignment = Assignment.DirectClaim(
                message.Slots.Single().Id,
                message.Id,
                messageVolunteer.Id,
                Now);
            var unconfirmedAssignment = Assignment.Create(
                unconfirmed.Slots.Single().Id,
                unconfirmed.Id,
                unconfirmedVolunteer.Id,
                null,
                Coordinator,
                Now);
            var pendingRequest = ShiftRequest.Create(
                pending.Slots.Single().Id,
                pendingVolunteer.Id,
                Now);
            seed.Assignments.AddRange(messageAssignment, unconfirmedAssignment);
            seed.ShiftRequests.Add(pendingRequest);

            var intent = NotificationIntent.Create(
                "message-transition",
                messageAssignment.Id,
                messageVolunteer.Id,
                message.Slots.Single().Id,
                "AssignmentAccess",
                Now);
            intent.Fail(Now, "Delivery failed");
            seed.NotificationIntents.Add(intent);
            var attempt = NotificationAttempt.Create(messageAssignment.Id, "AssignmentAccess", Now);
            attempt.Fail(Now, "Delivery failed");
            seed.NotificationAttempts.Add(attempt);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(
            context,
            new ScheduleTestHelpers.FixedClock(Now));
        var page = await service.GetCoordinatorWorkPageAsync(
            new CoordinatorWorkFilter(),
            null,
            null,
            default);

        var uncoveredIndex = page.Items.ToList().FindIndex(x => x.Category == "uncovered");
        var messageIndex = page.Items.ToList().FindIndex(x => x.Category == "message");
        var unconfirmedIndex = page.Items.ToList().FindIndex(x => x.Category == "unconfirmed");
        var pendingIndex = page.Items.ToList().FindIndex(x => x.Category == "pending-request");
        Assert.True(uncoveredIndex >= 0);
        Assert.True(messageIndex > uncoveredIndex);
        Assert.True(unconfirmedIndex > messageIndex);
        Assert.True(pendingIndex > unconfirmedIndex);
        Assert.Single(page.Items, x => x.Category == "message");
        Assert.Equal("coverage-unconfirmed", page.Items.Single(x => x.Category == "unconfirmed").RouteKind);

        static Shift CreatePublishedShift(string title, DateTimeOffset startsAtUtc)
        {
            var shift = Shift.Create(
                title,
                null,
                null,
                startsAtUtc,
                startsAtUtc.AddHours(1),
                0,
                SignupPolicy.ApprovalRequired);
            shift.Publish(Now);
            return shift;
        }
    }

    [Fact]
    public async Task HistoryKeysetPagesRemainStableAndFilterable()
    {
        await _fixture.ResetAsync();
        await using (var seed = _fixture.CreateContext())
        {
            seed.GroupSettings.Add(VolunteerCoordinator.Domain.Settings.GroupSettings.Create("Etc/UTC"));
            for (var index = 0; index < 55; index++)
            {
                seed.AuditEntries.Add(AuditEntry.Create(
                    Now.AddMinutes(index),
                    Coordinator,
                    "ShiftPublished",
                    "Shift",
                    Guid.NewGuid(),
                    "{}"));
            }

            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(
            context,
            new ScheduleTestHelpers.FixedClock(Now));
        var filter = new AuditHistoryFilter(Category: "Schedule");
        var first = await service.GetAuditHistoryPageAsync(filter, null, default);
        var cursor = new AuditHistoryCursor(
            first.Items[^1].OccurredAtUtc,
            first.Items[^1].AuditId);
        var second = await service.GetAuditHistoryPageAsync(filter, cursor, default);

        Assert.Equal(50, first.Items.Count);
        Assert.Equal(5, second.Items.Count);
        Assert.Empty(first.Items.Select(x => x.AuditId).Intersect(second.Items.Select(x => x.AuditId)));
        Assert.All(first.Items.Concat(second.Items), item => Assert.Equal("Schedule", item.Category));
    }

    [Fact]
    public async Task AccessDiagnosticsRequireTwoCurrentVerifiedIdentities()
    {
        await _fixture.ResetAsync();
        await using (var seed = _fixture.CreateContext())
        {
            seed.GroupSettings.Add(VolunteerCoordinator.Domain.Settings.GroupSettings.Create("Etc/UTC"));
            seed.AuditEntries.Add(AuditEntry.Create(
                Now,
                "FIRST@EXAMPLE.ORG",
                "CoordinatorAccessVerified",
                "Coordinator",
                Guid.Empty,
                "{}"));
            seed.AuditEntries.Add(AuditEntry.Create(
                Now.AddMinutes(1),
                "SECOND@EXAMPLE.ORG",
                "CoordinatorAccessVerified",
                "Coordinator",
                Guid.Empty,
                "{}"));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(
            context,
            new ScheduleTestHelpers.FixedClock(Now));
        var diagnostics = await service.GetCoordinatorAccessDiagnosticsAsync(
            true,
            [" first@example.org ", "SECOND@example.org", "second@example.org"],
            "first@example.org",
            default);

        Assert.True(diagnostics.OidcConfigured);
        Assert.Equal(["FIRST@EXAMPLE.ORG", "SECOND@EXAMPLE.ORG"], diagnostics.AllowlistedEmails);
        Assert.Equal("FIRST@EXAMPLE.ORG", diagnostics.CurrentEmail);
        Assert.True(diagnostics.HandoffReady);
    }
    [Fact]
    public async Task HistoryClassifiesVolunteerActionsAndHumanizesRecurringWorker()
    {
        await _fixture.ResetAsync();
        var recurringEntityId = Guid.NewGuid();
        var volunteerActionId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            seed.GroupSettings.Add(VolunteerCoordinator.Domain.Settings.GroupSettings.Create("Etc/UTC"));
            seed.AuditEntries.Add(AuditEntry.Create(
                Now,
                "recurring-worker",
                "RecurringOccurrenceGenerated",
                "RecurringShiftOccurrence",
                recurringEntityId,
                "{}",
                shiftId: Guid.NewGuid()));
            seed.AuditEntries.Add(AuditEntry.Create(
                Now.AddMinutes(1),
                "volunteer-token",
                "AssignmentConfirm",
                "Assignment",
                volunteerActionId,
                "{}",
                shiftId: Guid.NewGuid(),
                volunteerId: Guid.NewGuid()));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(
            context,
            new ScheduleTestHelpers.FixedClock(Now));
        var recurring = await service.GetAuditHistoryPageAsync(
            new AuditHistoryFilter(Category: "Recurring schedules"),
            null,
            default);
        var volunteer = await service.GetAuditHistoryPageAsync(
            new AuditHistoryFilter(Category: "Volunteer actions"),
            null,
            default);
        var actors = await service.GetAuditActorsAsync(default);

        var recurringEntry = Assert.Single(recurring.Items);
        Assert.Equal("Recurring schedule process", recurringEntry.ActorDisplay);
        Assert.Contains("generated", recurringEntry.Summary, StringComparison.OrdinalIgnoreCase);
        var volunteerEntry = Assert.Single(volunteer.Items);
        Assert.Contains("confirmed", volunteerEntry.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("A recorded coordinator action occurred.", volunteerEntry.Summary, StringComparison.Ordinal);
        Assert.Contains(actors, actor =>
            actor.Value == "recurring-worker" &&
            actor.Label == "Recurring schedule process");
    }
}
