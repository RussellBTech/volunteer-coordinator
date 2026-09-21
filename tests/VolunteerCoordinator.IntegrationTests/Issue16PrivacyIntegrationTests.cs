using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Access;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Settings;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Volunteers;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue16PrivacyIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public Issue16PrivacyIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EligibleAnonymizationInvalidatesLinksRedactsNotificationsAndKeepsHistory()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        Guid volunteerId;
        string originalEmail;
        string statusToken;
        string actionToken;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Historical commitment",
                "Community hall",
                null,
                FixedNow.AddDays(-400),
                FixedNow.AddDays(-399),
                0,
                Coordinator);
            var shift = await context.Shifts
                .Include(x => x.Slots)
                .SingleAsync(x => x.Id == shiftId);
            var volunteer = Volunteer.Create(
                "Alex Rivera",
                "alex-privacy@example.org",
                "555-0100",
                FixedNow.AddDays(-400));
            originalEmail = volunteer.Email;
            context.Volunteers.Add(volunteer);
            volunteerId = volunteer.Id;

            var tokenService = new SecureTokenService();
            var generatedStatus = tokenService.Generate();
            statusToken = generatedStatus.RawToken;
            var request = ShiftRequest.Create(shift.Slots.Single().Id, volunteer.Id, FixedNow.AddDays(-400));
            request.Approve(Coordinator, FixedNow.AddDays(-399));
            context.ShiftRequests.Add(request);
            context.VolunteerAccessCapabilities.Add(VolunteerAccessCapability.Create(
                shift.Slots.Single().Id,
                volunteer.Id,
                generatedStatus.Hash,
                FixedNow.AddDays(-400),
                CapabilityIssuedReason.Request));

            var assignment = Assignment.Create(
                shift.Slots.Single().Id,
                shift.Id,
                volunteer.Id,
                request.Id,
                Coordinator,
                FixedNow.AddDays(-400));
            assignment.Reassign(FixedNow.AddDays(-399));
            context.Assignments.Add(assignment);
            var generatedAction = tokenService.Generate();
            actionToken = generatedAction.RawToken;
            context.ActionTokens.Add(ActionToken.Create(
                assignment.Id,
                VolunteerAction.Confirm,
                generatedAction.Hash,
                FixedNow.AddDays(-400),
                FixedNow.AddDays(-399)));

            var notification = NotificationAttempt.Create(
                request.Id,
                "RequestReceived",
                originalEmail,
                FixedNow.AddDays(-400));
            notification.Fail(FixedNow.AddDays(-399), "safe provider error");
            context.NotificationAttempts.Add(notification);
            await context.SaveChangesAsync();

            var result = await service.AnonymizeVolunteerAsync(
                volunteer.Id,
                VolunteerAnonymizationReason.RetentionExpired,
                "ignored-for-retention",
                default);
            Assert.Equal(VolunteerAnonymizationOutcome.Anonymized, result.Outcome);
            Assert.Equal(1, result.CapabilitiesInvalidated);
            Assert.Equal(1, result.ActionTokensInvalidated);
            Assert.Equal(0, result.NotificationDestinationsRedacted);
        }

        await using (var verification = _fixture.CreateContext())
        {
            var volunteer = await verification.Volunteers.SingleAsync(x => x.Id == volunteerId);
            Assert.Equal("Removed volunteer", volunteer.Name);
            Assert.Equal($"removed-{volunteerId:N}@invalid.invalid", volunteer.Email);
            Assert.Equal(volunteer.Email.ToUpperInvariant(), volunteer.NormalizedEmail);
            Assert.Null(volunteer.Phone);
            Assert.NotNull(volunteer.AnonymizedAtUtc);
            var capability = await verification.VolunteerAccessCapabilities.SingleAsync();
            Assert.NotNull(capability.InvalidatedAtUtc);
            Assert.NotNull((await verification.ActionTokens.SingleAsync()).UsedAtUtc);
            var persistedAttempts = await verification.NotificationAttempts.ToListAsync();
            Assert.DoesNotContain(
                originalEmail,
                System.Text.Json.JsonSerializer.Serialize(persistedAttempts),
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(originalEmail, await verification.AuditEntries.Select(x => x.DetailJson).ToListAsync());
        }

        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            await Assert.ThrowsAsync<DomainException>(() => service.GetRequestStatusAsync(statusToken, default));
            await Assert.ThrowsAsync<DomainException>(() => service.ApplyActionAsync(actionToken, default));

            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Replacement commitment",
                null,
                null,
                FixedNow.AddDays(2),
                FixedNow.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            var submission = await service.SubmitRequestAsync(
                shift.Slots.Single().Id,
                "Alex Rivera",
                originalEmail,
                null,
                default);
            Assert.NotEqual(volunteerId, (await context.ShiftRequests.SingleAsync(x => x.Id == submission.RequestId)).VolunteerId);
            Assert.Equal(2, await context.Volunteers.CountAsync());
        }
    }

    [Fact]
    public async Task AnonymizationRollsBackWhenMinimalAuditCannotCommit()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        Guid volunteerId;
        await using (var seedContext = _fixture.CreateContext())
        {
            var volunteer = Volunteer.Create(
                "Rollback volunteer",
                "rollback@example.org",
                "555-0100",
                FixedNow.AddDays(-400));
            seedContext.Volunteers.Add(volunteer);
            await seedContext.SaveChangesAsync();
            volunteerId = volunteer.Id;
        }

        await using var triggerContext = _fixture.CreateContext();
        await triggerContext.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION issue16_raise_on_anonymization()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF NEW."Action" = 'VolunteerAnonymized' THEN
                    RAISE EXCEPTION 'issue sixteen rollback test';
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER issue16_raise_on_anonymization_trigger
            BEFORE INSERT ON "AuditEntries"
            FOR EACH ROW
            EXECUTE FUNCTION issue16_raise_on_anonymization();
            """);
        try
        {
            var exception = await Record.ExceptionAsync(() =>
                ScheduleTestHelpers.CreateService(triggerContext, clock).AnonymizeVolunteerAsync(
                    volunteerId,
                    VolunteerAnonymizationReason.RetentionExpired,
                    "retention-worker",
                    default));
            Assert.NotNull(exception);
        }
        finally
        {
            await triggerContext.Database.ExecuteSqlRawAsync(
                """
                DROP TRIGGER issue16_raise_on_anonymization_trigger ON "AuditEntries";
                DROP FUNCTION issue16_raise_on_anonymization();
                """);
        }

        await using var verification = _fixture.CreateContext();
        var volunteerState = await verification.Volunteers.SingleAsync(x => x.Id == volunteerId);
        Assert.Equal("Rollback volunteer", volunteerState.Name);
        Assert.Equal("rollback@example.org", volunteerState.Email);
        Assert.Equal("555-0100", volunteerState.Phone);
        Assert.Null(volunteerState.AnonymizedAtUtc);
        Assert.Empty(await verification.AuditEntries.Where(x => x.Action == "VolunteerAnonymized").ToListAsync());
    }

    [Fact]
    public async Task RetentionAnchorUsesEveryApprovedActivitySource()
    {
        await _fixture.ResetAsync();
        var anchorSources = new[]
        {
            "RequestRequested",
            "RequestResolved",
            "AssignmentAssigned",
            "AssignmentConfirmed",
            "AssignmentEnded",
            "NotificationCreated",
            "NotificationCompleted",
            "ShiftEnd"
        };
        var volunteerIds = new List<Guid>();
        await using (var context = _fixture.CreateContext())
        {
            foreach (var (source, index) in anchorSources.Select((source, index) => (source, index)))
            {
                var shiftEndsAt = FixedNow.AddDays(-400);
                if (source == "ShiftEnd")
                {
                    shiftEndsAt = FixedNow.AddDays(-200);
                }

                var shift = Shift.Create(
                    $"Anchor {source}",
                    null,
                    null,
                    FixedNow.AddDays(-401),
                    shiftEndsAt,
                    0);
                var volunteer = Volunteer.Create(
                    $"Anchor {source}",
                    $"anchor-{index}@example.org",
                    null,
                    FixedNow.AddDays(-400));
                context.Shifts.Add(shift);
                context.Volunteers.Add(volunteer);
                volunteerIds.Add(volunteer.Id);

                var requestedAt = FixedNow.AddDays(-400);
                var resolvedAt = FixedNow.AddDays(-400);
                if (source == "RequestRequested")
                {
                    requestedAt = FixedNow.AddDays(-200);
                }
                else if (source == "RequestResolved")
                {
                    resolvedAt = FixedNow.AddDays(-200);
                }

                var request = ShiftRequest.Create(shift.Slots.Single().Id, volunteer.Id, requestedAt);
                request.Approve(Coordinator, resolvedAt);
                context.ShiftRequests.Add(request);

                if (source is "AssignmentAssigned" or "AssignmentConfirmed" or "AssignmentEnded")
                {
                    var assignedAt = source == "AssignmentAssigned"
                        ? FixedNow.AddDays(-200)
                        : FixedNow.AddDays(-400);
                    var assignment = Assignment.Create(
                        shift.Slots.Single().Id,
                        shift.Id,
                        volunteer.Id,
                        request.Id,
                        Coordinator,
                        assignedAt);
                    if (source == "AssignmentConfirmed")
                    {
                        assignment.Confirm(FixedNow.AddDays(-200));
                        assignment.Reassign(FixedNow.AddDays(-400));
                    }
                    else if (source == "AssignmentEnded")
                    {
                        assignment.Reassign(FixedNow.AddDays(-200));
                    }
                    else
                    {
                        assignment.Reassign(FixedNow.AddDays(-400));
                    }

                    context.Assignments.Add(assignment);
                }
                else if (source is "NotificationCreated" or "NotificationCompleted")
                {
                    var notificationCreatedAt = source == "NotificationCreated"
                        ? FixedNow.AddDays(-200)
                        : FixedNow.AddDays(-400);
                    var notificationCompletedAt = source == "NotificationCompleted"
                        ? FixedNow.AddDays(-200)
                        : FixedNow.AddDays(-400);
                    var notification = NotificationAttempt.Create(
                        request.Id,
                        source,
                        $"anchor-{index}@example.org",
                        notificationCreatedAt);
                    notification.Fail(notificationCompletedAt, "safe error");
                    context.NotificationAttempts.Add(notification);
                }
            }

            await context.SaveChangesAsync();
        }

        await using var verificationContext = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(
            verificationContext,
            new ScheduleTestHelpers.FixedClock(FixedNow));
        foreach (var volunteerId in volunteerIds)
        {
            var result = await service.AnonymizeVolunteerAsync(
                volunteerId,
                VolunteerAnonymizationReason.RetentionExpired,
                "retention-worker",
                default);
            Assert.Equal(VolunteerAnonymizationOutcome.Blocked, result.Outcome);
            Assert.Equal(VolunteerAnonymizationBlocker.TooRecent, result.Blocker);
        }
    }

    [Fact]
    public async Task RetentionUsesExactBoundaryAndBlocksFutureCommitments()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await using (var context = _fixture.CreateContext())
        {
            var eligible = Volunteer.Create(
                "Boundary",
                "boundary@example.org",
                null,
                FixedNow.AddDays(-365));
            var tooRecent = Volunteer.Create(
                "Recent",
                "recent@example.org",
                null,
                FixedNow.AddDays(-365).AddTicks(10));
            context.Volunteers.AddRange(eligible, tooRecent);
            await context.SaveChangesAsync();
            var service = ScheduleTestHelpers.CreateService(context, clock);

            var exact = await service.AnonymizeVolunteerAsync(
                eligible.Id,
                VolunteerAnonymizationReason.RetentionExpired,
                "retention-worker",
                default);
            var recent = await service.AnonymizeVolunteerAsync(
                tooRecent.Id,
                VolunteerAnonymizationReason.RetentionExpired,
                "retention-worker",
                default);
            Assert.Equal(VolunteerAnonymizationOutcome.Anonymized, exact.Outcome);
            Assert.Equal(VolunteerAnonymizationOutcome.Blocked, recent.Outcome);
            Assert.Equal(VolunteerAnonymizationBlocker.TooRecent, recent.Blocker);
        }

        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Future commitment",
                null,
                null,
                FixedNow.AddDays(1),
                FixedNow.AddDays(2),
                0,
                Coordinator);
            var shift = await context.Shifts.Include(x => x.Slots).SingleAsync(x => x.Id == shiftId);
            var volunteer = Volunteer.Create(
                "Future",
                "future@example.org",
                null,
                FixedNow.AddDays(-400));
            context.Volunteers.Add(volunteer);
            var assignment = Assignment.Create(
                shift.Slots.Single().Id,
                shift.Id,
                volunteer.Id,
                null,
                Coordinator,
                FixedNow.AddDays(-400));
            assignment.Reassign(FixedNow.AddDays(-399));
            context.Assignments.Add(assignment);
            await context.SaveChangesAsync();

            var blocked = await service.AnonymizeVolunteerAsync(
                volunteer.Id,
                VolunteerAnonymizationReason.CoordinatorRequest,
                Coordinator,
                default);
            Assert.Equal(VolunteerAnonymizationOutcome.Blocked, blocked.Outcome);
            Assert.Equal(VolunteerAnonymizationBlocker.FutureCommitments, blocked.Blocker);
            Assert.Equal(1, await context.AuditEntries.CountAsync(x => x.Action == "VolunteerAnonymized"));
        }
    }

    [Fact]
    public async Task FutureDateShiftEditWinsRaceAndBlocksAnonymizationWithoutMutation()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var candidate = await SeedShiftEditRaceCandidateAsync(clock);

        await using var heldContext = _fixture.CreateContext();
        await using var heldTransaction = await heldContext.Database.BeginTransactionAsync();
        await LockShiftAsync(heldContext, candidate.ShiftId);

        await using var editContext = _fixture.CreateContext();
        var editTask = ScheduleTestHelpers.EditShiftFromInstantsAsync(
            ScheduleTestHelpers.CreateService(editContext, clock),
            candidate.ShiftId,
            candidate.ShiftVersion,
            "Future edit",
            null,
            null,
            FixedNow.AddDays(1),
            FixedNow.AddDays(2),
            0,
            Coordinator,
            cancellationToken: default);
        await ScheduleTestHelpers.AssertBlockedAsync(editTask);

        await using var removalContext = _fixture.CreateContext();
        var removalTask = ScheduleTestHelpers.CreateService(removalContext, clock).AnonymizeVolunteerAsync(
            candidate.VolunteerId,
            VolunteerAnonymizationReason.RetentionExpired,
            "retention-worker",
            default);
        await ScheduleTestHelpers.AssertBlockedAsync(removalTask);

        await heldTransaction.CommitAsync();
        await editTask;
        var removal = await removalTask;

        Assert.Equal(VolunteerAnonymizationOutcome.Blocked, removal.Outcome);
        Assert.Equal(VolunteerAnonymizationBlocker.FutureCommitments, removal.Blocker);

        await using var verification = _fixture.CreateContext();
        var volunteer = await verification.Volunteers.SingleAsync(x => x.Id == candidate.VolunteerId);
        Assert.Null(volunteer.AnonymizedAtUtc);
        Assert.Equal(candidate.Email, volunteer.Email);
        Assert.Null((await verification.ActionTokens.SingleAsync()).UsedAtUtc);
        Assert.DoesNotContain(
            candidate.Email,
            System.Text.Json.JsonSerializer.Serialize(await verification.NotificationAttempts.ToListAsync()),
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await verification.AuditEntries.Where(x => x.Action == "VolunteerAnonymized").ToListAsync());
        Assert.Equal(
            FixedNow.AddDays(2),
            (await verification.Shifts.SingleAsync(x => x.Id == candidate.ShiftId)).EndsAtUtc);
    }

    [Fact]
    public async Task AnonymizationWinsRaceAndFutureDateShiftEditCommitsAfterRemoval()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var candidate = await SeedShiftEditRaceCandidateAsync(clock);

        await using var heldContext = _fixture.CreateContext();
        await using var heldTransaction = await heldContext.Database.BeginTransactionAsync();
        await LockShiftAsync(heldContext, candidate.ShiftId);

        await using var removalContext = _fixture.CreateContext();
        var removalTask = ScheduleTestHelpers.CreateService(removalContext, clock).AnonymizeVolunteerAsync(
            candidate.VolunteerId,
            VolunteerAnonymizationReason.RetentionExpired,
            "retention-worker",
            default);
        await ScheduleTestHelpers.AssertBlockedAsync(removalTask);

        await using var editContext = _fixture.CreateContext();
        var editTask = ScheduleTestHelpers.EditShiftFromInstantsAsync(
            ScheduleTestHelpers.CreateService(editContext, clock),
            candidate.ShiftId,
            candidate.ShiftVersion,
            "Future edit",
            null,
            null,
            FixedNow.AddDays(1),
            FixedNow.AddDays(2),
            0,
            Coordinator,
            cancellationToken: default);
        await ScheduleTestHelpers.AssertBlockedAsync(editTask);

        await heldTransaction.CommitAsync();
        var removal = await removalTask;
        await editTask;

        Assert.Equal(VolunteerAnonymizationOutcome.Anonymized, removal.Outcome);

        await using var verification = _fixture.CreateContext();
        var volunteer = await verification.Volunteers.SingleAsync(x => x.Id == candidate.VolunteerId);
        Assert.Equal("Removed volunteer", volunteer.Name);
        Assert.NotNull(volunteer.AnonymizedAtUtc);
        var capability = await verification.VolunteerAccessCapabilities.SingleAsync();
        Assert.NotNull(capability.InvalidatedAtUtc);
        Assert.NotNull((await verification.ActionTokens.SingleAsync()).UsedAtUtc);
        Assert.DoesNotContain(
            candidate.Email,
            System.Text.Json.JsonSerializer.Serialize(await verification.NotificationAttempts.ToListAsync()),
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(await verification.AuditEntries.Where(x => x.Action == "VolunteerAnonymized").ToListAsync());
        Assert.Equal(
            FixedNow.AddDays(2),
            (await verification.Shifts.SingleAsync(x => x.Id == candidate.ShiftId)).EndsAtUtc);
    }

    [Fact]
    public async Task CoordinatorConfirmationAcceptsEveryDisplayedCommitmentWithDuplicateRequestsAndAssignments()
    {
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        for (var selectionIndex = 0; selectionIndex < 10; selectionIndex++)
        {
            await _fixture.ResetAsync();
            var candidate = await SeedDuplicateRemovalCandidateAsync();

            await using var lookupContext = _fixture.CreateContext();
            var lookup = await ScheduleTestHelpers.CreateService(lookupContext, clock)
                .LookupVolunteerRemovalAsync(candidate.Email, default);

            Assert.NotNull(lookup);
            Assert.Equal(10, lookup!.Commitments.Count);
            var selectedShiftId = lookup.Commitments[selectionIndex].ShiftId;

            await using var confirmationContext = _fixture.CreateContext();
            var result = await ScheduleTestHelpers.CreateService(confirmationContext, clock)
                .ConfirmVolunteerRemovalAsync(
                    candidate.VolunteerId,
                    selectedShiftId,
                    Volunteer.NormalizeEmail(candidate.Email),
                    Coordinator,
                    default);

            Assert.Equal(VolunteerAnonymizationOutcome.Anonymized, result.Outcome);
        }
    }

    [Fact]
    public async Task CoordinatorConfirmationRevalidatesNormalizedEmailForLockedVolunteer()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var candidate = await SeedDuplicateRemovalCandidateAsync();
        Guid alternateVolunteerId;
        Guid alternateShiftId;
        await using (var context = _fixture.CreateContext())
        {
            var alternate = Volunteer.Create(
                "Alternate volunteer",
                "alternate-removal@example.org",
                null,
                FixedNow.AddDays(-10));
            var shift = Shift.Create(
                "Alternate commitment",
                null,
                null,
                FixedNow.AddDays(-3),
                FixedNow.AddDays(-2),
                0);
            var request = ShiftRequest.Create(shift.Slots.Single().Id, alternate.Id, FixedNow.AddDays(-3));
            request.Reject(Coordinator, FixedNow.AddDays(-2));
            context.Volunteers.Add(alternate);
            context.Shifts.Add(shift);
            context.ShiftRequests.Add(request);
            await context.SaveChangesAsync();
            alternateVolunteerId = alternate.Id;
            alternateShiftId = shift.Id;
        }

        await using var confirmationContext = _fixture.CreateContext();
        var result = await ScheduleTestHelpers.CreateService(confirmationContext, clock)
            .ConfirmVolunteerRemovalAsync(
                alternateVolunteerId,
                alternateShiftId,
                Volunteer.NormalizeEmail(candidate.Email),
                Coordinator,
                default);

        Assert.Equal(VolunteerAnonymizationOutcome.NotFound, result.Outcome);
        await using var verification = _fixture.CreateContext();
        Assert.All(
            await verification.Volunteers.ToListAsync(),
            volunteer => Assert.Null(volunteer.AnonymizedAtUtc));
        Assert.Empty(await verification.AuditEntries.Where(x => x.Action == "VolunteerAnonymized").ToListAsync());
    }

    [Fact]
    public async Task CoordinatorConfirmationRejectsCommitmentOutsideBoundedDisplay()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var candidate = await SeedDuplicateRemovalCandidateAsync();
        Guid outsideTopTenShiftId;
        await using (var lookupContext = _fixture.CreateContext())
        {
            var lookup = await ScheduleTestHelpers.CreateService(lookupContext, clock)
                .LookupVolunteerRemovalAsync(candidate.Email, default);
            Assert.NotNull(lookup);
            var displayedShiftIds = lookup!.Commitments.Select(x => x.ShiftId).ToHashSet();
            outsideTopTenShiftId = (await lookupContext.Shifts
                .Select(x => x.Id)
                .ToListAsync())
                .Single(shiftId => !displayedShiftIds.Contains(shiftId));
        }

        await using var confirmationContext = _fixture.CreateContext();
        var result = await ScheduleTestHelpers.CreateService(confirmationContext, clock)
            .ConfirmVolunteerRemovalAsync(
                candidate.VolunteerId,
                outsideTopTenShiftId,
                Volunteer.NormalizeEmail(candidate.Email),
                Coordinator,
                default);

        Assert.Equal(VolunteerAnonymizationOutcome.NotFound, result.Outcome);
        await using var verification = _fixture.CreateContext();
        var volunteer = await verification.Volunteers.SingleAsync(x => x.Id == candidate.VolunteerId);
        Assert.Null(volunteer.AnonymizedAtUtc);
        Assert.Empty(await verification.AuditEntries.Where(x => x.Action == "VolunteerAnonymized").ToListAsync());
    }

    [Fact]
    public async Task CoordinatorConfirmationRejectsStaleSelectionAfterBoundedLookupChanges()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var candidate = await SeedDuplicateRemovalCandidateAsync();
        Guid staleShiftId;
        await using (var lookupContext = _fixture.CreateContext())
        {
            var lookup = await ScheduleTestHelpers.CreateService(lookupContext, clock)
                .LookupVolunteerRemovalAsync(candidate.Email, default);
            Assert.NotNull(lookup);
            staleShiftId = lookup!.Commitments[^1].ShiftId;
        }

        await using (var context = _fixture.CreateContext())
        {
            var shift = Shift.Create(
                "Newer commitment",
                null,
                null,
                FixedNow.AddDays(-1),
                FixedNow,
                0);
            var request = ShiftRequest.Create(shift.Slots.Single().Id, candidate.VolunteerId, FixedNow.AddDays(-1));
            request.Reject(Coordinator, FixedNow);
            context.Shifts.Add(shift);
            context.ShiftRequests.Add(request);
            await context.SaveChangesAsync();
        }

        await using var confirmationContext = _fixture.CreateContext();
        var result = await ScheduleTestHelpers.CreateService(confirmationContext, clock)
            .ConfirmVolunteerRemovalAsync(
                candidate.VolunteerId,
                staleShiftId,
                Volunteer.NormalizeEmail(candidate.Email),
                Coordinator,
                default);

        Assert.Equal(VolunteerAnonymizationOutcome.NotFound, result.Outcome);
        await using var verification = _fixture.CreateContext();
        var volunteer = await verification.Volunteers.SingleAsync(x => x.Id == candidate.VolunteerId);
        Assert.Null(volunteer.AnonymizedAtUtc);
        Assert.Empty(await verification.AuditEntries.Where(x => x.Action == "VolunteerAnonymized").ToListAsync());
    }


    [Fact]
    public async Task ContactReuseAndAnonymizationSerializeOnVolunteerRow()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        Guid volunteerId;
        Guid slotId;
        await using (var seedContext = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(seedContext, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Lock race",
                null,
                null,
                FixedNow.AddDays(2),
                FixedNow.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
            var volunteer = Volunteer.Create(
                "Lock race volunteer",
                "lock-race@example.org",
                null,
                FixedNow.AddDays(-400));
            seedContext.Volunteers.Add(volunteer);
            await seedContext.SaveChangesAsync();
            volunteerId = volunteer.Id;
        }

        await using var lockContext = _fixture.CreateContext();
        await using var lockTransaction = await lockContext.Database.BeginTransactionAsync();
        await new EfWorkflowStore(lockContext).LockVolunteerAsync(volunteerId, default);

        await using var requestContext = _fixture.CreateContext();
        await using var removalContext = _fixture.CreateContext();
        var requestTask = ScheduleTestHelpers.CreateService(requestContext, clock).SubmitRequestAsync(
            slotId,
            "Lock race replacement",
            "lock-race@example.org",
            null,
            default);
        await ScheduleTestHelpers.AssertBlockedAsync(requestTask);
        var removalTask = ScheduleTestHelpers.CreateService(removalContext, clock).AnonymizeVolunteerAsync(
            volunteerId,
            VolunteerAnonymizationReason.RetentionExpired,
            "retention-worker",
            default);
        await ScheduleTestHelpers.AssertBlockedAsync(removalTask);
        await lockTransaction.CommitAsync();
        await Task.WhenAll(removalTask, requestTask);
        var removalResult = await removalTask;
        Assert.Contains(
            removalResult.Outcome,
            new[]
            {
                VolunteerAnonymizationOutcome.Anonymized,
                VolunteerAnonymizationOutcome.Blocked
            });

        await using var verification = _fixture.CreateContext();
        var volunteerState = await verification.Volunteers.SingleAsync(x => x.Id == volunteerId);
        var requests = await verification.ShiftRequests.ToListAsync();
        if (volunteerState.AnonymizedAtUtc.HasValue)
        {
            Assert.DoesNotContain(requests, request => request.VolunteerId == volunteerId && request.Status == RequestStatus.Pending);
        }
        else
        {
            Assert.Contains(requests, request => request.VolunteerId == volunteerId && request.Status == RequestStatus.Pending);
            Assert.Empty(await verification.AuditEntries.Where(x => x.Action == "VolunteerAnonymized").ToListAsync());
        }
    }

    [Fact]
    public async Task ConcurrentAnonymizationProducesOneAuditAndRetentionBatchIsBounded()
    {
        await _fixture.ResetAsync();
        Guid firstId;
        Guid secondId;
        await using (var context = _fixture.CreateContext())
        {
            var first = Volunteer.Create("First", "first@example.org", null, FixedNow.AddDays(-400));
            var second = Volunteer.Create("Second", "second@example.org", null, FixedNow.AddDays(-400));
            context.Volunteers.AddRange(first, second);
            await context.SaveChangesAsync();
            firstId = first.Id;
            secondId = second.Id;
        }

        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var firstTask = ScheduleTestHelpers.CreateService(firstContext, clock).AnonymizeVolunteerAsync(
            firstId,
            VolunteerAnonymizationReason.RetentionExpired,
            "retention-worker",
            default);
        var secondTask = ScheduleTestHelpers.CreateService(secondContext, clock).AnonymizeVolunteerAsync(
            firstId,
            VolunteerAnonymizationReason.RetentionExpired,
            "retention-worker",
            default);
        var results = await Task.WhenAll(firstTask, secondTask);
        Assert.Contains(results, x => x.Outcome == VolunteerAnonymizationOutcome.Anonymized);
        Assert.Contains(results, x => x.Outcome == VolunteerAnonymizationOutcome.AlreadyAnonymized);

        await using (var verification = _fixture.CreateContext())
        {
            Assert.Equal(1, await verification.AuditEntries.CountAsync(x => x.Action == "VolunteerAnonymized"));
        }

        await using (var batchContext = _fixture.CreateContext())
        {
            var result = await ScheduleTestHelpers.CreateService(batchContext, clock)
                .RunRetentionSweepAsync(365, 1, default);
            Assert.Single(result);
            Assert.False(result[0].Failed);
            Assert.Equal(secondId, result[0].VolunteerId);
        }
    }

    [Fact]
    public async Task RetentionKeysetProgressesPastLeadingBlockedCandidate()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        Guid blockedId;
        Guid eligibleId;
        await using (var context = _fixture.CreateContext())
        {
            context.GroupSettings.Add(GroupSettings.Create("Etc/UTC"));
            var shift = Shift.Create(
                "Retention fairness",
                null,
                null,
                FixedNow.AddDays(-400),
                FixedNow.AddDays(-399),
                0);
            var blocked = Volunteer.Create(
                "Blocked",
                "blocked-fairness@example.org",
                null,
                FixedNow.AddDays(-400));
            var eligible = Volunteer.Create(
                "Eligible",
                "eligible-fairness@example.org",
                null,
                FixedNow.AddDays(-400));
            context.Shifts.Add(shift);
            context.Volunteers.AddRange(blocked, eligible);
            await context.SaveChangesAsync();

            var leading = await context.Volunteers
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArrayAsync();
            blockedId = leading[0];
            eligibleId = leading[1];
            var token = new SecureTokenService().Generate();
            context.ShiftRequests.Add(ShiftRequest.Create(shift.Slots.Single().Id, blockedId, FixedNow.AddDays(-400)));
            await context.SaveChangesAsync();
        }

        await using var sweepContext = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(sweepContext, clock);
        var first = await service.RunRetentionSweepAsync(365, 1, default);
        Assert.Single(first);
        Assert.Equal(blockedId, first[0].VolunteerId);
        Assert.Equal(VolunteerAnonymizationBlocker.PendingRequests, first[0].Result!.Blocker);

        var second = await service.RunRetentionSweepAsync(
            365,
            1,
            default,
            first[0].VolunteerId);
        Assert.Single(second);
        Assert.Equal(eligibleId, second[0].VolunteerId);
        Assert.Equal(VolunteerAnonymizationOutcome.Anonymized, second[0].Result!.Outcome);
    }

    [Fact]
    public async Task TerminalWorkflowNotificationReloadsAfterConcurrentRemoval()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        Guid volunteerId;
        Guid requestId;
        await using (var context = _fixture.CreateContext())
        {
            context.GroupSettings.Add(GroupSettings.Create("Etc/UTC"));
            var shift = Shift.Create(
                "Terminal notification race",
                null,
                null,
                FixedNow.AddDays(-3),
                FixedNow.AddDays(-2),
                0);
            var volunteer = Volunteer.Create(
                "Race volunteer",
                "race-notification@example.org",
                null,
                FixedNow.AddDays(-400));
            var request = ShiftRequest.Create(
                shift.Slots.Single().Id,
                volunteer.Id,
                FixedNow.AddDays(-3));
            context.Shifts.Add(shift);
            context.Volunteers.Add(volunteer);
            context.ShiftRequests.Add(request);
            await context.SaveChangesAsync();
            volunteerId = volunteer.Id;
            requestId = request.Id;
        }

        await using var workflowContext = _fixture.CreateContext();
        var blockingNotifications = new BlockingNotificationService(
            new UnavailableNotificationService(workflowContext, clock));
        var workflowService = new VolunteerCoordinatorService(
            new EfWorkflowStore(workflowContext),
            clock,
            new SecureTokenService(),
            blockingNotifications);
        var terminalWorkflow = workflowService.RejectRequestAsync(requestId, Coordinator, default);
        var notificationMessage = await blockingNotifications.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(volunteerId, notificationMessage.VolunteerId);
        await using var removalContext = _fixture.CreateContext();
        var removal = await ScheduleTestHelpers.CreateService(removalContext, clock)
            .AnonymizeVolunteerAsync(
                volunteerId,
                VolunteerAnonymizationReason.CoordinatorRequest,
                Coordinator,
                default);
        Assert.Equal(VolunteerAnonymizationOutcome.Anonymized, removal.Outcome);

        blockingNotifications.Release.TrySetResult();
        var terminalResult = await terminalWorkflow;
        Assert.NotNull(terminalResult.NotificationWarning);
        Assert.Contains("skipped", terminalResult.NotificationWarning, StringComparison.OrdinalIgnoreCase);
        await using var verification = _fixture.CreateContext();
        var volunteerState = await verification.Volunteers.SingleAsync(x => x.Id == volunteerId);
        Assert.Equal("Removed volunteer", volunteerState.Name);
        Assert.Empty(await verification.NotificationAttempts.ToListAsync());
        Assert.DoesNotContain(
            "race-notification@example.org",
            System.Text.Json.JsonSerializer.Serialize(await verification.NotificationAttempts.ToListAsync()),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemovalLookupReturnsOnlyBoundedCommitmentProjection()
    {
        await _fixture.ResetAsync();
        await using (var context = _fixture.CreateContext())
        {
            context.GroupSettings.Add(GroupSettings.Create("Etc/UTC"));
            var volunteer = Volunteer.Create(
                "Projection volunteer",
                "projection@example.org",
                null,
                FixedNow.AddDays(-30));
            context.Volunteers.Add(volunteer);
            for (var index = 0; index < 25; index++)
            {
                var shift = Shift.Create(
                    $"Projection commitment {index}",
                    null,
                    null,
                    FixedNow.AddDays(-(index + 3)),
                    FixedNow.AddDays(-(index + 2)),
                    0);
                var request = ShiftRequest.Create(shift.Slots.Single().Id, volunteer.Id, FixedNow.AddDays(-(index + 3)));
                context.Shifts.Add(shift);
                context.ShiftRequests.Add(request);
            }

            await context.SaveChangesAsync();
        }

        await using var lookupContext = _fixture.CreateContext();
        var lookup = await ScheduleTestHelpers.CreateService(
                lookupContext,
                new ScheduleTestHelpers.FixedClock(FixedNow))
            .LookupVolunteerRemovalAsync("projection@example.org", default);

        Assert.NotNull(lookup);
        Assert.Equal(10, lookup!.Commitments.Count);
        Assert.All(lookup.Commitments, commitment =>
            Assert.StartsWith("Projection commitment", commitment.Commitment.ShiftTitle, StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitialRetentionProcessesAllCandidatesInBoundedBatches()
    {
        await _fixture.ResetAsync();
        var volunteers = new[]
        {
            Volunteer.Create("Initial one", "initial-one@example.org", null, FixedNow.AddDays(-400)),
            Volunteer.Create("Initial two", "initial-two@example.org", null, FixedNow.AddDays(-401)),
            Volunteer.Create("Initial three", "initial-three@example.org", null, FixedNow.AddDays(-402))
        };
        await using (var context = _fixture.CreateContext())
        {
            context.Volunteers.AddRange(volunteers);
            await context.SaveChangesAsync();
        }

        await using var sweepContext = _fixture.CreateContext();
        await ScheduleTestHelpers.CreateService(
                sweepContext,
                new ScheduleTestHelpers.FixedClock(FixedNow))
            .RunInitialRetentionSweepAsync(365, 1, default);

        await using var verification = _fixture.CreateContext();
        Assert.Equal(
            3,
            await verification.Volunteers.CountAsync(x => x.AnonymizedAtUtc == FixedNow));
        Assert.Equal(
            3,
            await verification.AuditEntries.CountAsync(x => x.Action == "VolunteerAnonymized"));
    }

    [Fact]
    public async Task PrivacyMigrationAddsLifecycleColumnsToIssueFifteenSchema()
    {
        await using var context = _fixture.CreateContext();
        var applied = context.Database.GetAppliedMigrations().ToArray();
        Assert.Contains(applied, migration => migration.Contains("PresentCommitments", StringComparison.Ordinal));
        Assert.Contains(applied, migration => migration.Contains("VolunteerPrivacyLifecycle", StringComparison.Ordinal));
        Assert.True(await context.Database.CanConnectAsync());
        var volunteerColumns = await context.Database
            .SqlQueryRaw<string>(
                """SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = 'Volunteers'""")
            .ToListAsync();
        Assert.Contains("AnonymizedAtUtc", volunteerColumns);
        var requestColumns = await context.Database
            .SqlQueryRaw<string>(
                """SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = 'ShiftRequests'""")
            .ToListAsync();
        Assert.DoesNotContain("StatusTokenHash", requestColumns);
        Assert.DoesNotContain("StatusTokenExpiresAtUtc", requestColumns);
        Assert.DoesNotContain("StatusTokenInvalidatedAtUtc", requestColumns);
    }
    private async Task<ShiftEditRaceCandidate> SeedShiftEditRaceCandidateAsync(
        ScheduleTestHelpers.FixedClock clock)
    {
        await using var context = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Shift edit race",
            null,
            null,
            FixedNow.AddDays(-400),
            FixedNow.AddDays(-399),
            0,
            Coordinator);
        var shift = await context.Shifts
            .Include(x => x.Slots)
            .SingleAsync(x => x.Id == shiftId);
        var volunteer = Volunteer.Create(
            "Shift edit race volunteer",
            "shift-edit-race@example.org",
            "555-0116",
            FixedNow.AddDays(-400));
        context.Volunteers.Add(volunteer);

        var tokenService = new SecureTokenService();
        var statusToken = tokenService.Generate();
        var request = ShiftRequest.Create(shift.Slots.Single().Id, volunteer.Id, FixedNow.AddDays(-400));
        request.Approve(Coordinator, FixedNow.AddDays(-399));
        context.ShiftRequests.Add(request);
        context.VolunteerAccessCapabilities.Add(VolunteerAccessCapability.Create(
            shift.Slots.Single().Id,
            volunteer.Id,
            statusToken.Hash,
            FixedNow.AddDays(-400),
            CapabilityIssuedReason.Request));

        var assignment = Assignment.Create(
            shift.Slots.Single().Id,
            shift.Id,
            volunteer.Id,
            request.Id,
            Coordinator,
            FixedNow.AddDays(-400));
        assignment.Reassign(FixedNow.AddDays(-399));
        context.Assignments.Add(assignment);

        var actionToken = tokenService.Generate();
        context.ActionTokens.Add(ActionToken.Create(
            assignment.Id,
            VolunteerAction.Confirm,
            actionToken.Hash,
            FixedNow.AddDays(-400),
            FixedNow.AddDays(-399)));

        var notification = NotificationAttempt.Create(
            request.Id,
            "RequestReceived",
            volunteer.Email,
            FixedNow.AddDays(-400));
        notification.Fail(FixedNow.AddDays(-399), "safe provider error");
        context.NotificationAttempts.Add(notification);
        await context.SaveChangesAsync();

        return new ShiftEditRaceCandidate(
            volunteer.Id,
            shift.Id,
            shift.Version,
            assignment.Id,
            volunteer.Email);
    }

    private async Task<DuplicateRemovalCandidate> SeedDuplicateRemovalCandidateAsync()
    {
        await using var context = _fixture.CreateContext();
        context.GroupSettings.Add(GroupSettings.Create("Etc/UTC"));
        var volunteer = Volunteer.Create(
            "Duplicate removal volunteer",
            "duplicate-removal@example.org",
            null,
            FixedNow.AddDays(-400));
        var shifts = Enumerable.Range(0, 11)
            .Select(index => Shift.Create(
                $"Duplicate commitment {index}",
                null,
                null,
                FixedNow.AddDays(-401),
                FixedNow.AddDays(-400),
                0))
            .ToArray();
        context.Volunteers.Add(volunteer);
        context.Shifts.AddRange(shifts);
        await context.SaveChangesAsync();

        var orderedShifts = shifts.OrderBy(x => x.Id).ToArray();
        var requests = new List<ShiftRequest>();
        var assignments = new List<Assignment>();
        var relationshipIndex = 0;
        foreach (var shift in orderedShifts)
        {
            relationshipIndex++;
            var request = ShiftRequest.Create(shift.Slots.Single().Id, volunteer.Id, FixedNow.AddDays(-401));
            request.Reject(Coordinator, FixedNow.AddDays(-400));
            requests.Add(request);

            var assignment = Assignment.Create(
                shift.Slots.Single().Id,
                shift.Id,
                volunteer.Id,
                request.Id,
                Coordinator,
                FixedNow.AddDays(-401));
            assignment.Reassign(FixedNow.AddDays(-400));
            assignments.Add(assignment);
        }

        for (var duplicateIndex = 0; duplicateIndex < 9; duplicateIndex++)
        {
            relationshipIndex++;
            var request = ShiftRequest.Create(orderedShifts[0].Slots.Single().Id, volunteer.Id, FixedNow.AddDays(-401));
            request.Reject(Coordinator, FixedNow.AddDays(-400));
            requests.Add(request);

            var assignment = Assignment.Create(
                orderedShifts[0].Slots.Single().Id,
                orderedShifts[0].Id,
                volunteer.Id,
                request.Id,
                Coordinator,
                FixedNow.AddDays(-401));
            assignment.Reassign(FixedNow.AddDays(-400));
            assignments.Add(assignment);
        }

        context.ShiftRequests.AddRange(requests);
        context.Assignments.AddRange(assignments);
        await context.SaveChangesAsync();
        return new DuplicateRemovalCandidate(volunteer.Id, volunteer.Email);
    }

    private static Task<int> LockShiftAsync(
        VolunteerCoordinatorDbContext context,
        Guid shiftId) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Shifts" WHERE "Id" = {shiftId} FOR UPDATE""");

    private sealed record ShiftEditRaceCandidate(
        Guid VolunteerId,
        Guid ShiftId,
        uint ShiftVersion,
        Guid AssignmentId,
        string Email);

    private sealed record DuplicateRemovalCandidate(Guid VolunteerId, string Email);

    private sealed class BlockingNotificationService : INotificationService
    {
        private readonly INotificationService _inner;

        public BlockingNotificationService(INotificationService inner)
        {
            _inner = inner;
        }

        public TaskCompletionSource<NotificationMessage> Called { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<NotificationResult> RecordAndSendAsync(
            NotificationMessage message,
            CancellationToken cancellationToken)
        {
            Called.TrySetResult(message);
            await Release.Task;
            return await _inner.RecordAndSendAsync(message, cancellationToken);
        }
    }
}
