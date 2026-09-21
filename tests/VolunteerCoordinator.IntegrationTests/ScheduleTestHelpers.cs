using Microsoft.EntityFrameworkCore;
using Xunit;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Application.Time;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using VolunteerCoordinator.Infrastructure.Time;

namespace VolunteerCoordinator.IntegrationTests;

internal static class ScheduleTestHelpers
{
    public static async Task<GroupSettingsDto> EnsureSettingsAsync(
        VolunteerCoordinatorService service,
        string coordinatorEmail,
        CancellationToken cancellationToken = default)
    {
        return await service.GetGroupSettingsAsync(cancellationToken)
            ?? await service.ConfigureGroupTimeZoneAsync(
                "Etc/UTC",
                null,
                false,
                coordinatorEmail,
                cancellationToken);
    }

    public static LocalScheduleInput ForInstantRange(
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        GroupSettingsDto settings)
    {
        if (!TimeZoneLabels.TryGetIanaZone(settings.TimeZoneId, out _, out var timeZone))
        {
            throw new InvalidOperationException("The test settings must contain a valid IANA zone.");
        }

        var startsAtLocal = DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTime(startsAtUtc.ToUniversalTime(), timeZone).DateTime,
            DateTimeKind.Unspecified);
        var endsAtLocal = DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTime(endsAtUtc.ToUniversalTime(), timeZone).DateTime,
            DateTimeKind.Unspecified);
        return new LocalScheduleInput(startsAtLocal, endsAtLocal, null, null, settings.Version);
    }

    public static async Task<Guid> CreateShiftFromInstantsAsync(
        VolunteerCoordinatorService service,
        string title,
        string? location,
        string? notes,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        int backupSlotCount,
        string coordinatorEmail,
        string? volunteerInstructions = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await EnsureSettingsAsync(service, coordinatorEmail, cancellationToken);
        return await service.CreateShiftAsync(
            title,
            location,
            notes,
            volunteerInstructions,
            ForInstantRange(startsAtUtc, endsAtUtc, settings),
            backupSlotCount,
            coordinatorEmail,
            cancellationToken);
    }

    public static async Task EditShiftFromInstantsAsync(
        VolunteerCoordinatorService service,
        Guid shiftId,
        uint expectedVersion,
        string title,
        string? location,
        string? notes,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        int backupSlotCount,
        string coordinatorEmail,
        string? volunteerInstructions = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await EnsureSettingsAsync(service, coordinatorEmail, cancellationToken);
        await service.EditShiftAsync(
            shiftId,
            expectedVersion,
            title,
            location,
            notes,
            volunteerInstructions,
            ForInstantRange(startsAtUtc, endsAtUtc, settings),
            backupSlotCount,
            coordinatorEmail,
            cancellationToken);
    }

    public static VolunteerCoordinatorService CreateService(
        VolunteerCoordinatorDbContext context,
        IClock? clock = null)
    {
        clock ??= new SystemClock();
        return new VolunteerCoordinatorService(
            new EfWorkflowStore(context),
            clock,
            new SecureTokenService(),
            new UnavailableNotificationService(context, clock));
    }

    public static async Task<string> CreateLegacyActionTokenAsync(
        VolunteerCoordinatorDbContext context,
        Guid assignmentId,
        VolunteerAction action,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc = null)
    {
        var existing = await context.ActionTokens
            .Where(x => x.AssignmentId == assignmentId && x.Action == action && x.UsedAtUtc == null)
            .ToListAsync();
        foreach (var token in existing)
        {
            token.Invalidate(createdAtUtc);
        }

        var generated = new SecureTokenService().Generate();
        context.ActionTokens.Add(ActionToken.Create(
            assignmentId,
            action,
            generated.Hash,
            createdAtUtc,
            expiresAtUtc ?? createdAtUtc.AddDays(7)));
        await context.SaveChangesAsync();
        return generated.RawToken;
    }

    public static async Task<(string? ConfirmToken, string? DeclineToken, string? CancelToken)> CreateLegacyActionLinksAsync(
        VolunteerCoordinatorDbContext context,
        Guid assignmentId,
        AssignmentStatus status,
        DateTimeOffset createdAtUtc)
    {
        var confirm = status == AssignmentStatus.Assigned
            ? await CreateLegacyActionTokenAsync(context, assignmentId, VolunteerAction.Confirm, createdAtUtc)
            : null;
        var decline = status == AssignmentStatus.Assigned
            ? await CreateLegacyActionTokenAsync(context, assignmentId, VolunteerAction.Decline, createdAtUtc)
            : null;
        var cancel = await CreateLegacyActionTokenAsync(context, assignmentId, VolunteerAction.Cancel, createdAtUtc);
        return (confirm, decline, cancel);
    }

    public static async Task AssertBlockedAsync(Task operation)
    {
        var timeout = Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.Same(timeout, await Task.WhenAny(operation, timeout));
    }

    public sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
