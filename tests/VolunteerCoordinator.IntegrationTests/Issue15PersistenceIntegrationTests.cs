using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue15PersistenceIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlFixture _fixture;

    public Issue15PersistenceIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InitialConfigurationPersistsSingletonAndAuditsNormalizedActor()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var service = CreateService(context);

        var settings = await service.ConfigureGroupTimeZoneAsync(
            "  America/New_York  ",
            null,
            false,
            "Coordinator@Example.org",
            default);

        var persisted = await EntityFrameworkQueryableExtensions.SingleAsync(context.GroupSettings);
        Assert.Equal("America/New_York", persisted.TimeZoneId);
        Assert.Equal(settings.Version, persisted.Version);
        var audit = Assert.Single(await EntityFrameworkQueryableExtensions.ToListAsync(context.AuditEntries));
        Assert.Equal("COORDINATOR@EXAMPLE.ORG", audit.Actor);
        Assert.Equal("GroupTimeZoneConfigured", audit.Action);
        using var detail = JsonDocument.Parse(audit.DetailJson);
        Assert.Equal("America/New_York", detail.RootElement.GetProperty("NewTimeZoneId").GetString());
    }

    [Fact]
    public async Task StaleZoneChangeLeavesSettingsAndAuditUnchanged()
    {
        await _fixture.ResetAsync();
        await using var setupContext = _fixture.CreateContext();
        var setupService = CreateService(setupContext);
        var initial = await setupService.ConfigureGroupTimeZoneAsync(
            "America/New_York",
            null,
            false,
            Coordinator,
            default);

        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        await CreateService(firstContext).ConfigureGroupTimeZoneAsync(
            "America/Chicago",
            initial.Version,
            true,
            Coordinator,
            default);

        await Assert.ThrowsAsync<DomainException>(() =>
            CreateService(secondContext).ConfigureGroupTimeZoneAsync(
                "Europe/London",
                initial.Version,
                true,
                Coordinator,
                default));

        await using var verification = _fixture.CreateContext();
        Assert.Equal("America/Chicago", (await EntityFrameworkQueryableExtensions.SingleAsync(verification.GroupSettings)).TimeZoneId);
        Assert.Equal(
            1,
            await EntityFrameworkQueryableExtensions.CountAsync(verification.AuditEntries, x => x.Action == "GroupTimeZoneChanged"));
    }

    [Fact]
    public async Task InstructionsProjectToVolunteersAndZoneChangePreservesUtcInstants()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var settings = await service.ConfigureGroupTimeZoneAsync(
            "America/New_York",
            null,
            false,
            Coordinator,
            default);
        var startsLocal = new DateTime(2026, 10, 3, 10, 0, 0);
        var endsLocal = new DateTime(2026, 10, 3, 12, 30, 0);
        var shiftId = await service.CreateShiftAsync(
            "Food service",
            "Community hall",
            "Internal route",
            "Use the north entrance.",
            new LocalScheduleInput(startsLocal, endsLocal, null, null, settings.Version),
            0,
            Coordinator,
            default);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var before = await EntityFrameworkQueryableExtensions.SingleAsync(context.Shifts, x => x.Id == shiftId);
        var startsUtc = before.StartsAtUtc;
        var endsUtc = before.EndsAtUtc;
        var opening = Assert.Single(await service.ListOpeningsAsync(default));
        Assert.Equal("Use the north entrance.", opening.Commitment.VolunteerInstructions);
        Assert.DoesNotContain("Internal route", JsonSerializer.Serialize(opening.Commitment));
        Assert.Equal("Internal route", (await service.ListShiftsAsync(default)).Single().InternalCoordinatorNotes);

        var changed = await service.ConfigureGroupTimeZoneAsync(
            "Asia/Tokyo",
            settings.Version,
            true,
            Coordinator,
            default);

        Assert.NotEqual(settings.Version, changed.Version);
        await using var verification = _fixture.CreateContext();
        var after = await EntityFrameworkQueryableExtensions.SingleAsync(verification.Shifts, x => x.Id == shiftId);
        Assert.Equal(startsUtc, after.StartsAtUtc);
        Assert.Equal(endsUtc, after.EndsAtUtc);
        var changedOpening = Assert.Single(await CreateService(verification).ListOpeningsAsync(default));
        Assert.Equal("Asia/Tokyo", changedOpening.Commitment.GroupTimeZoneId);
        Assert.Equal(opening.Commitment.StartsAtUtc, changedOpening.Commitment.StartsAtUtc);
    }


    [Fact]
    public async Task InvalidZoneAndScheduleValidationDoNotBootstrapOrLeaveAuditState()
    {
        await _fixture.ResetAsync();
        var fixedNow = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using var context = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(
            context,
            new ScheduleTestHelpers.FixedClock(fixedNow));

        await Assert.ThrowsAsync<DomainException>(() =>
            service.ConfigureGroupTimeZoneAsync(
                "Not/AZone",
                null,
                false,
                Coordinator,
                default));

        var unconfiguredInput = new LocalScheduleInput(
            new DateTime(2026, 10, 3, 10, 0, 0),
            new DateTime(2026, 10, 3, 11, 0, 0),
            null,
            null,
            0);
        await Assert.ThrowsAsync<DomainException>(() =>
            service.CreateShiftAsync(
                "Must not bootstrap",
                null,
                null,
                null,
                unconfiguredInput,
                0,
                Coordinator,
                default));

        await using (var unconfiguredVerification = _fixture.CreateContext())
        {
            Assert.Empty(await unconfiguredVerification.GroupSettings.ToListAsync());
            Assert.Empty(await unconfiguredVerification.Shifts.ToListAsync());
            Assert.Empty(await unconfiguredVerification.AuditEntries.ToListAsync());
        }

        var settings = await service.ConfigureGroupTimeZoneAsync(
            "America/New_York",
            null,
            false,
            Coordinator,
            default);
        var gapInput = new LocalScheduleInput(
            new DateTime(2026, 3, 8, 2, 30, 0),
            new DateTime(2026, 3, 8, 4, 0, 0),
            null,
            null,
            settings.Version);
        await Assert.ThrowsAsync<DomainException>(() =>
            service.CreateShiftAsync(
                "Invalid local time",
                null,
                null,
                null,
                gapInput,
                0,
                Coordinator,
                default));

        await using var verification = _fixture.CreateContext();
        Assert.Empty(await verification.Shifts.ToListAsync());
        Assert.Single(await verification.AuditEntries.ToListAsync());
    }

    [Fact]
    public async Task StaleExpectedSettingsVersionRejectsScheduleMutationWithoutShiftAudit()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(
            context,
            new ScheduleTestHelpers.FixedClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)));
        var initial = await service.ConfigureGroupTimeZoneAsync(
            "America/New_York",
            null,
            false,
            Coordinator,
            default);
        await service.ConfigureGroupTimeZoneAsync(
            "America/Chicago",
            initial.Version,
            true,
            Coordinator,
            default);

        var staleInput = new LocalScheduleInput(
            new DateTime(2026, 10, 3, 10, 0, 0),
            new DateTime(2026, 10, 3, 11, 0, 0),
            null,
            null,
            initial.Version);
        await Assert.ThrowsAsync<DomainException>(() =>
            service.CreateShiftAsync(
                "Stale schedule",
                null,
                null,
                null,
                staleInput,
                0,
                Coordinator,
                default));

        await using var verification = _fixture.CreateContext();
        Assert.Empty(await verification.Shifts.ToListAsync());
        Assert.Equal("America/Chicago", (await verification.GroupSettings.SingleAsync()).TimeZoneId);
        Assert.Equal(2, await verification.AuditEntries.CountAsync());
        Assert.DoesNotContain(
            await verification.AuditEntries.ToListAsync(),
            entry => entry.Action == "ShiftCreated");
    }

    [Fact]
    public async Task HeldSettingsLockSerializesScheduleMutationAndZoneChangeAcrossContexts()
    {
        await _fixture.ResetAsync();
        var fixedNow = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using var setupContext = _fixture.CreateContext();
        var setupService = ScheduleTestHelpers.CreateService(
            setupContext,
            new ScheduleTestHelpers.FixedClock(fixedNow));
        var settings = await setupService.ConfigureGroupTimeZoneAsync(
            "America/New_York",
            null,
            false,
            Coordinator,
            default);
        var schedule = new LocalScheduleInput(
            new DateTime(2026, 10, 3, 10, 0, 0),
            new DateTime(2026, 10, 3, 12, 0, 0),
            null,
            null,
            settings.Version);

        await using var lockContext = _fixture.CreateContext();
        var lockStore = new EfWorkflowStore(lockContext);
        var lockHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockTask = lockStore.ExecuteInTransactionAsync(
            async token =>
            {
                await lockStore.LockGroupSettingsAsync(token);
                lockHeld.SetResult(true);
                await releaseLock.Task.WaitAsync(token);
                return true;
            },
            default);
        await lockHeld.Task;

        await using var mutationContext = _fixture.CreateContext();
        var mutationService = ScheduleTestHelpers.CreateService(
            mutationContext,
            new ScheduleTestHelpers.FixedClock(fixedNow));
        var mutationTask = mutationService.CreateShiftAsync(
            "Serialized local shift",
            null,
            null,
            null,
            schedule,
            0,
            Coordinator,
            default);
        await ScheduleTestHelpers.AssertBlockedAsync(mutationTask);

        await using var changeContext = _fixture.CreateContext();
        var changeService = ScheduleTestHelpers.CreateService(
            changeContext,
            new ScheduleTestHelpers.FixedClock(fixedNow));
        var changeTask = changeService.ConfigureGroupTimeZoneAsync(
            "Asia/Tokyo",
            settings.Version,
            true,
            Coordinator,
            default);
        await ScheduleTestHelpers.AssertBlockedAsync(changeTask);

        releaseLock.SetResult(true);
        var mutationException = await Record.ExceptionAsync(() => mutationTask);
        var changeException = await Record.ExceptionAsync(() => changeTask);
        await lockTask;

        await using var verification = _fixture.CreateContext();
        var persistedShift = await verification.Shifts.SingleOrDefaultAsync();
        if (mutationException is null)
        {
            Assert.Null(changeException);
            Assert.NotNull(persistedShift);
            Assert.Equal(
                new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.Zero),
                persistedShift.StartsAtUtc);
            Assert.Equal("Asia/Tokyo", (await verification.GroupSettings.SingleAsync()).TimeZoneId);
        }
        else
        {
            Assert.IsType<DomainException>(mutationException);
            Assert.Null(changeException);
            Assert.Null(persistedShift);
            Assert.Equal("Asia/Tokyo", (await verification.GroupSettings.SingleAsync()).TimeZoneId);
        }
    }
    private static VolunteerCoordinatorService CreateService(VolunteerCoordinatorDbContext context)
    {
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        return new VolunteerCoordinatorService(
            new EfWorkflowStore(context),
            clock,
            new SecureTokenService(),
            new UnavailableNotificationService(context, clock));
    }
}
