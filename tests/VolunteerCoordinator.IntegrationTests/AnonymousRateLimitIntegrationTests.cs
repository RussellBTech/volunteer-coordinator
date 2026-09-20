using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using VolunteerCoordinator.Infrastructure.Time;
using VolunteerCoordinator.Web.Security;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class AnonymousRateLimitIntegrationTests
{
    private readonly PostgreSqlFixture _fixture;

    public AnonymousRateLimitIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void NonPositiveRateLimitConfigurationFailsStartup()
    {
        var limits = CreateLimits();
        limits.RequestMutation.PermitLimit = 0;
        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            rateLimits: limits);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("AnonymousRateLimits", exception.ToString());
    }

    [Fact]
    public async Task RequestMutationThrottleIsGenericAndDoesNotChangePersistence()
    {
        await _fixture.ResetAsync();
        var slotId = await CreatePublishedSlotAsync();
        var before = await ReadSnapshotAsync();
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, rateLimits: CreateLimits());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Rate limited",
            ["Email"] = "rate-limited@example.org"
        });

        var first = await client.PostAsync($"/Shifts/Request/{slotId}", form);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        var validRejected = await client.PostAsync($"/Shifts/Request/{slotId}", form);
        var invalidRejected = await client.PostAsync($"/Shifts/Request/{Guid.NewGuid()}", form);

        await AssertGenericThrottleAsync(validRejected);
        await AssertGenericThrottleAsync(invalidRejected);
        await AssertPersistenceUnchangedAsync(before);
    }

    [Fact]
    public async Task PrivateTokenReadThrottleIsIndependentFromOpeningListAndDoesNotMutate()
    {
        await _fixture.ResetAsync();
        var statusToken = await CreateRequestAsync();
        var before = await ReadSnapshotAsync();
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, rateLimits: CreateLimits());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var first = await client.GetAsync($"/Requests/Status/{statusToken}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var invalidRejected = await client.GetAsync("/Requests/Status/not-a-real-token");
        var validRejected = await client.GetAsync($"/Requests/Status/{statusToken}");
        var unaffected = await client.GetAsync("/Shifts");

        await AssertGenericThrottleAsync(invalidRejected);
        await AssertGenericThrottleAsync(validRejected);
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
        await AssertPersistenceUnchangedAsync(before);
    }

    [Fact]
    public async Task AssignmentMutationThrottleRunsBeforeAntiforgeryAndPreservesActionState()
    {
        await _fixture.ResetAsync();
        var actionToken = await CreateAssignmentActionTokenAsync();
        var before = await ReadSnapshotAsync();
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, rateLimits: CreateLimits());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var first = await client.PostAsync($"/Actions/{actionToken}", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        var validRejected = await client.PostAsync($"/Actions/{actionToken}", content: null);
        var invalidRejected = await client.PostAsync("/Actions/not-a-real-token", content: null);

        await AssertGenericThrottleAsync(validRejected);
        await AssertGenericThrottleAsync(invalidRejected);
        await AssertPersistenceUnchangedAsync(before);
    }

    [Fact]
    public async Task AnonymousTiersUseIndependentBuckets()
    {
        await _fixture.ResetAsync();
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, rateLimits: CreateLimits());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var firstRequest = await client.PostAsync($"/Shifts/Request/{Guid.NewGuid()}", content: null);
        var rejectedRequest = await client.PostAsync($"/Shifts/Request/{Guid.NewGuid()}", content: null);
        var privateRead = await client.GetAsync("/Requests/Status/not-a-real-token");
        var openingList = await client.GetAsync("/Shifts");

        Assert.Equal(HttpStatusCode.BadRequest, firstRequest.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedRequest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, privateRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openingList.StatusCode);
    }

    [Fact]
    public async Task TrailingSlashGetRoutesCannotBypassPrivateTokenReadLimiter()
    {
        await _fixture.ResetAsync();
        var statusToken = await CreateRequestAsync();
        var actionToken = await CreateAssignmentActionTokenAsync();
        var before = await ReadSnapshotAsync();
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, rateLimits: CreateLimits());
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        firstClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.12");
        secondClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.13");

        var statusAllowed = await firstClient.GetAsync($"/Requests/Status/{statusToken}/");
        var statusRejected = await firstClient.GetAsync($"/Requests/Status/{statusToken}/");
        var actionAllowed = await secondClient.GetAsync($"/Actions/{actionToken}/");
        var actionRejected = await secondClient.GetAsync($"/Actions/{actionToken}/");

        Assert.Equal(HttpStatusCode.OK, statusAllowed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, actionAllowed.StatusCode);
        await AssertGenericThrottleAsync(statusRejected);
        await AssertGenericThrottleAsync(actionRejected);
        await AssertPersistenceUnchangedAsync(before);
    }

    [Fact]
    public async Task TrailingSlashMutationRoutesCannotBypassPostLimiters()
    {
        await _fixture.ResetAsync();
        var slotId = await CreatePublishedSlotAsync();
        var actionToken = await CreateAssignmentActionTokenAsync();
        var before = await ReadSnapshotAsync();
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, rateLimits: CreateLimits());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Trailing slash",
            ["Email"] = "trailing-slash@example.org"
        });

        var requestAllowed = await client.PostAsync($"/Shifts/Request/{slotId}/", form);
        var requestRejected = await client.PostAsync($"/Shifts/Request/{slotId}/", form);
        var actionAllowed = await client.PostAsync($"/Actions/{actionToken}/", content: null);
        var actionRejected = await client.PostAsync($"/Actions/{actionToken}/", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, requestAllowed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, actionAllowed.StatusCode);
        await AssertGenericThrottleAsync(requestRejected);
        await AssertGenericThrottleAsync(actionRejected);
        await AssertPersistenceUnchangedAsync(before);
    }

    [Fact]
    public async Task RequestCompletionPostsAreUnlimitedAndDoNotConsumeRequestMutationBucket()
    {
        await _fixture.ResetAsync();
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, rateLimits: CreateLimits());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        foreach (var completionPath in new[]
        {
            "/Shifts/Request/Complete",
            "/Shifts/Request/Complete",
            "/Shifts/Request/Complete/"
        })
        {
            var completionResponse = await client.PostAsync(completionPath, content: null);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, completionResponse.StatusCode);
        }

        var malformedAllowed = await client.PostAsync("/Shifts/Request/not-a-guid", content: null);
        var malformedTrailingSlashRejected = await client.PostAsync(
            "/Shifts/Request/not-a-guid/",
            content: null);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, malformedAllowed.StatusCode);
        await AssertGenericThrottleAsync(malformedTrailingSlashRejected);
    }


    [Fact]
    public async Task ClientIpPartitionsAreIndependent()
    {
        await _fixture.ResetAsync();
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, rateLimits: CreateLimits());
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        firstClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");
        secondClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.11");

        var firstClientAllowed = await firstClient.PostAsync(
            $"/Shifts/Request/{Guid.NewGuid()}",
            content: null);
        var secondClientAllowed = await secondClient.PostAsync(
            $"/Shifts/Request/{Guid.NewGuid()}",
            content: null);
        var firstClientRejected = await firstClient.PostAsync(
            $"/Shifts/Request/{Guid.NewGuid()}",
            content: null);
        var secondClientRejected = await secondClient.PostAsync(
            $"/Shifts/Request/{Guid.NewGuid()}",
            content: null);

        Assert.Equal(HttpStatusCode.BadRequest, firstClientAllowed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, secondClientAllowed.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, firstClientRejected.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondClientRejected.StatusCode);
    }

    private async Task<Guid> CreatePublishedSlotAsync()
    {
        await using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Rate-limit shift",
            null,
            null,
            starts,
            starts.AddHours(1),
            0,
            "coordinator@example.org");
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, "coordinator@example.org", default);
        return (await service.ListOpeningsAsync(default)).Single(x => x.ShiftId == shiftId).SlotId;
    }

    private async Task<string> CreateRequestAsync()
    {
        var slotId = await CreatePublishedSlotAsync();
        await using var context = _fixture.CreateContext();
        var submission = await CreateService(context).SubmitRequestAsync(
            slotId,
            "Status Volunteer",
            "status-volunteer@example.org",
            null,
            default);
        return submission.StatusToken;
    }

    private async Task<string> CreateAssignmentActionTokenAsync()
    {
        var slotId = await CreatePublishedSlotAsync();
        await using var context = _fixture.CreateContext();
        var assignment = await CreateService(context).AssignDirectlyAsync(
            slotId,
            "Action Volunteer",
            "action-volunteer@example.org",
            null,
            "coordinator@example.org",
            default);
        var links = await CreateService(context).GenerateActionLinksAsync(
            assignment.AssignmentId,
            "coordinator@example.org",
            default);
        return links.ConfirmToken!;
    }

    private async Task<PersistenceSnapshot> ReadSnapshotAsync()
    {
        await using var context = _fixture.CreateContext();
        var shifts = await context.Shifts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var slots = await context.ShiftSlots.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var volunteers = await context.Volunteers.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var requests = await context.ShiftRequests.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var assignments = await context.Assignments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var actionTokens = await context.ActionTokens.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var notifications = await context.NotificationAttempts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var audits = await context.AuditEntries.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();

        return new PersistenceSnapshot(
            shifts.Select(x => new ShiftSnapshot(
                x.Id,
                x.Title,
                x.Location,
                x.Notes,
                x.StartsAtUtc,
                x.EndsAtUtc,
                x.IsActive,
                x.PublishedAtUtc,
                x.UpdatedAtUtc,
                x.Version)).ToArray(),
            slots.Select(x => new SlotSnapshot(
                x.Id,
                x.ShiftId,
                x.Kind,
                x.Position,
                x.IsActive)).ToArray(),
            volunteers.Select(x => new VolunteerSnapshot(
                x.Id,
                x.Name,
                x.Email,
                x.NormalizedEmail,
                x.Phone,
                x.CreatedAtUtc,
                x.UpdatedAtUtc)).ToArray(),
            requests.Select(x => new RequestSnapshot(
                x.Id,
                x.ShiftSlotId,
                x.VolunteerId,
                x.Status,
                x.RequestedAtUtc,
                x.ResolvedAtUtc,
                x.ResolvedByCoordinatorEmail,
                Convert.ToHexString(x.StatusTokenHash),
                x.StatusTokenExpiresAtUtc)).ToArray(),
            assignments.Select(x => new AssignmentSnapshot(
                x.Id,
                x.ShiftSlotId,
                x.ShiftId,
                x.VolunteerId,
                x.SourceRequestId,
                x.Status,
                x.AssignedAtUtc,
                x.ConfirmedAtUtc,
                x.EndedAtUtc,
                x.AssignedByCoordinatorEmail)).ToArray(),
            actionTokens.Select(x => new ActionTokenSnapshot(
                x.Id,
                x.AssignmentId,
                x.Action,
                Convert.ToHexString(x.TokenHash),
                x.CreatedAtUtc,
                x.ExpiresAtUtc,
                x.UsedAtUtc)).ToArray(),
            notifications.Select(x => new NotificationSnapshot(
                x.Id,
                x.TransitionId,
                x.Kind,
                x.Destination,
                x.State,
                x.CreatedAtUtc,
                x.CompletedAtUtc,
                x.ErrorSummary)).ToArray(),
            audits.Select(x => new AuditSnapshot(
                x.Id,
                x.OccurredAtUtc,
                x.Actor,
                x.Action,
                x.EntityKind,
                x.EntityId,
                x.DetailJson)).ToArray());
    }

    private async Task AssertPersistenceUnchangedAsync(PersistenceSnapshot expected)
    {
        var actual = await ReadSnapshotAsync();
        Assert.Equal(expected.Shifts, actual.Shifts);
        Assert.Equal(expected.Slots, actual.Slots);
        Assert.Equal(expected.Volunteers, actual.Volunteers);
        Assert.Equal(expected.Requests, actual.Requests);
        Assert.Equal(expected.Assignments, actual.Assignments);
        Assert.Equal(expected.ActionTokens, actual.ActionTokens);
        Assert.Equal(expected.Notifications, actual.Notifications);
        Assert.Equal(expected.Audits, actual.Audits);
    }

    private static VolunteerCoordinatorService CreateService(VolunteerCoordinatorDbContext context) =>
        new(
            new EfWorkflowStore(context),
            new SystemClock(),
            new SecureTokenService(),
            new UnavailableNotificationService(context, new SystemClock()));

    private static AnonymousRateLimitOptions CreateLimits() => new()
    {
        RequestMutation = new() { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) },
        PrivateTokenRead = new() { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) },
        AssignmentActionMutation = new() { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) }
    };

    private static async Task AssertGenericThrottleAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(
            "Too many requests. Try again later.",
            await response.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.True(int.TryParse(retryAfter.Single(), out var seconds));
        Assert.True(seconds > 0);
    }

    private sealed record PersistenceSnapshot(
        ShiftSnapshot[] Shifts,
        SlotSnapshot[] Slots,
        VolunteerSnapshot[] Volunteers,
        RequestSnapshot[] Requests,
        AssignmentSnapshot[] Assignments,
        ActionTokenSnapshot[] ActionTokens,
        NotificationSnapshot[] Notifications,
        AuditSnapshot[] Audits);

    private sealed record ShiftSnapshot(
        Guid Id,
        string Title,
        string? Location,
        string? Notes,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        bool IsActive,
        DateTimeOffset? PublishedAtUtc,
        DateTimeOffset? UpdatedAtUtc,
        uint Version);

    private sealed record SlotSnapshot(
        Guid Id,
        Guid ShiftId,
        SlotKind Kind,
        int Position,
        bool IsActive);

    private sealed record VolunteerSnapshot(
        Guid Id,
        string Name,
        string Email,
        string NormalizedEmail,
        string? Phone,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record RequestSnapshot(
        Guid Id,
        Guid ShiftSlotId,
        Guid VolunteerId,
        RequestStatus Status,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset? ResolvedAtUtc,
        string? ResolvedByCoordinatorEmail,
        string StatusTokenHash,
        DateTimeOffset StatusTokenExpiresAtUtc);

    private sealed record AssignmentSnapshot(
        Guid Id,
        Guid ShiftSlotId,
        Guid ShiftId,
        Guid VolunteerId,
        Guid? SourceRequestId,
        AssignmentStatus Status,
        DateTimeOffset AssignedAtUtc,
        DateTimeOffset? ConfirmedAtUtc,
        DateTimeOffset? EndedAtUtc,
        string AssignedByCoordinatorEmail);

    private sealed record ActionTokenSnapshot(
        Guid Id,
        Guid AssignmentId,
        VolunteerAction Action,
        string TokenHash,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? UsedAtUtc);

    private sealed record NotificationSnapshot(
        Guid Id,
        Guid TransitionId,
        string Kind,
        string Destination,
        NotificationState State,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        string? ErrorSummary);

    private sealed record AuditSnapshot(
        Guid Id,
        DateTimeOffset OccurredAtUtc,
        string Actor,
        string Action,
        string EntityKind,
        Guid EntityId,
        string DetailJson);
}
