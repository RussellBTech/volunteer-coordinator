using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using VolunteerCoordinator.Application.Ports;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue15WebIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlFixture _fixture;

    public Issue15WebIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UnconfiguredPublicRoutesUseUniformUnavailableResultWithoutMutation()
    {
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await _fixture.ResetAsync();
        Guid slotId;
        string statusToken;
        string actionToken;
        await using (var context = _fixture.CreateContext())
        {
            var service = CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Bootstrap shift",
                null,
                null,
                new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 3, 11, 0, 0, TimeSpan.Zero),
                1,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single(x => x.Kind == "Primary").Id;
            var assignmentSlotId = shift.Slots.Single(x => x.Kind == "Backup").Id;
            var submission = await service.SubmitRequestAsync(slotId, "Bootstrap volunteer", "bootstrap@example.org", null, default);
            statusToken = submission.StatusToken;
            var assignment = await service.AssignDirectlyAsync(assignmentSlotId, "Bootstrap volunteer", "bootstrap@example.org", null, Coordinator, default);
            actionToken = (await service.GenerateActionLinksAsync(assignment.AssignmentId, Coordinator, default)).ConfirmToken!;
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var requestForm = await client.GetAsync($"/Shifts/Request/{slotId}");
        var requestToken = ExtractAntiforgeryToken(await requestForm.Content.ReadAsStringAsync());
        var actionForm = await client.GetAsync($"/Actions/{actionToken}");
        var actionRequestToken = ExtractAntiforgeryToken(await actionForm.Content.ReadAsStringAsync());

        int requestsBefore;
        int assignmentsBefore;
        int actionTokensBefore;
        int notificationsBefore;
        int auditsBefore;
        await using (var countContext = _fixture.CreateContext())
        {
            requestsBefore = await countContext.ShiftRequests.CountAsync();
            assignmentsBefore = await countContext.Assignments.CountAsync();
            actionTokensBefore = await countContext.ActionTokens.CountAsync();
            notificationsBefore = await countContext.NotificationAttempts.CountAsync();
            auditsBefore = await countContext.AuditEntries.CountAsync();
        }

        await using (var deleteContext = _fixture.CreateContext())
        {
            await deleteContext.Database.ExecuteSqlRawAsync("DELETE FROM \"GroupSettings\"");
        }

        var expected = VolunteerCoordinatorService.CommitmentUnavailableMessage;
        var publicHtml = await ReadAsync(client, "/Shifts");
        var requestHtml = await ReadAsync(client, $"/Shifts/Request/{slotId}");
        var validStatusHtml = await ReadAsync(client, $"/Requests/Status/{statusToken}");
        var invalidStatusHtml = await ReadAsync(client, $"/Requests/Status/{Guid.NewGuid():N}");
        var validActionHtml = await ReadAsync(client, $"/Actions/{actionToken}");
        var invalidActionHtml = await ReadAsync(client, "/Actions/not-a-valid-token");

        Assert.Contains(expected, publicHtml);
        Assert.Contains(expected, requestHtml);
        Assert.Equal(validStatusHtml, invalidStatusHtml);
        Assert.Equal(validActionHtml, invalidActionHtml);
        Assert.Contains(expected, validStatusHtml);
        Assert.Contains(expected, validActionHtml);
        Assert.DoesNotContain("UTC", validStatusHtml, StringComparison.OrdinalIgnoreCase);

        var requestPost = await client.PostAsync(
            $"/Shifts/Request/{slotId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = requestToken,
                ["Name"] = "Should not mutate",
                ["Email"] = "should-not-mutate@example.org"
            }));
        Assert.Equal(HttpStatusCode.OK, requestPost.StatusCode);

        var actionPost = await client.PostAsync(
            $"/Actions/{actionToken}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = actionRequestToken
            }));
        Assert.Equal(HttpStatusCode.OK, actionPost.StatusCode);
        Assert.Contains(expected, await actionPost.Content.ReadAsStringAsync());

        var invalidRequestPost = await client.PostAsync(
            $"/Shifts/Request/{Guid.NewGuid()}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = requestToken,
                ["Name"] = "Should not mutate",
                ["Email"] = "invalid-request@example.org"
            }));
        var invalidActionPost = await client.PostAsync(
            "/Actions/not-a-valid-token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = actionRequestToken
            }));
        Assert.Equal(requestPost.StatusCode, invalidRequestPost.StatusCode);
        Assert.Equal(actionPost.StatusCode, invalidActionPost.StatusCode);
        Assert.Equal(
            await requestPost.Content.ReadAsStringAsync(),
            await invalidRequestPost.Content.ReadAsStringAsync());
        Assert.Equal(
            await actionPost.Content.ReadAsStringAsync(),
            await invalidActionPost.Content.ReadAsStringAsync());

        await using var verification = _fixture.CreateContext();
        Assert.Equal(requestsBefore, await verification.ShiftRequests.CountAsync());
        Assert.Equal(assignmentsBefore, await verification.Assignments.CountAsync());
        Assert.Equal(actionTokensBefore, await verification.ActionTokens.CountAsync());
        Assert.Equal(notificationsBefore, await verification.NotificationAttempts.CountAsync());
        Assert.Equal(auditsBefore, await verification.AuditEntries.CountAsync());
    }

    [Fact]
    public async Task ConfiguredSurfacesRenderCompleteSharedCommitmentDetails()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        Guid slotId;
        Guid requestId;
        string statusToken;
        Guid assignmentId;
        string actionToken;
        await using (var context = _fixture.CreateContext())
        {
            var service = CreateService(context, clock);
            var settings = await service.ConfigureGroupTimeZoneAsync("America/Chicago", null, false, Coordinator, default);
            var shiftId = await service.CreateShiftAsync(
                "Community meal",
                "Community hall",
                "Internal route",
                "Use the north entrance.",
                new LocalScheduleInput(
                    new DateTime(2026, 11, 3, 18, 30, 0),
                    new DateTime(2026, 11, 3, 20, 0, 0),
                    null,
                    null,
                    settings.Version),
                0,
                Coordinator,
                default);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
            _ = Assert.Single(await service.ListOpeningsAsync(default));
            var submission = await service.SubmitRequestAsync(slotId, "Jordan", "jordan@example.org", null, default);
            requestId = submission.RequestId;
            statusToken = submission.StatusToken;
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var publicClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var openingHtml = await ReadAsync(publicClient, "/Shifts");
        var requestHtml = await ReadAsync(publicClient, $"/Shifts/Request/{slotId}");
        foreach (var html in new[] { openingHtml, requestHtml })
        {
            Assert.Contains("Tuesday, November 3, 2026, 6:30 PM CST (UTC-06:00)", html);
            Assert.Contains("Tuesday, November 3, 2026, 8:00 PM CST (UTC-06:00)", html);
            Assert.Contains("1 hour 30 minutes", html);
            Assert.Contains("Community hall", html);
            Assert.Contains("Use the north entrance.", html);
            Assert.Contains("datetime=\"2026-11-04T00:30:00Z\"", html);
            Assert.Contains("datetime=\"2026-11-04T02:00:00Z\"", html);
            Assert.DoesNotContain("Internal route", html);
        }

        await using (var approvalContext = _fixture.CreateContext())
        {
            var service = CreateService(approvalContext, clock);
            var assignment = await service.ApproveRequestAsync(requestId, Coordinator, default);
            assignmentId = assignment.AssignmentId;
            actionToken = (await service.GenerateActionLinksAsync(assignmentId, Coordinator, default)).ConfirmToken!;
        }

        var statusHtml = await ReadAsync(publicClient, $"/Requests/Status/{statusToken}");
        var actionHtml = await ReadAsync(publicClient, $"/Actions/{actionToken}");
        foreach (var html in new[] { statusHtml, actionHtml })
        {
            Assert.Contains("Tuesday, November 3, 2026, 6:30 PM CST (UTC-06:00)", html);
            Assert.Contains("Tuesday, November 3, 2026, 8:00 PM CST (UTC-06:00)", html);
            Assert.Contains("1 hour 30 minutes", html);
            Assert.Contains("Community hall", html);
            Assert.Contains("Use the north entrance.", html);
            Assert.Contains("datetime=\"2026-11-04T00:30:00Z\"", html);
            Assert.Contains("datetime=\"2026-11-04T02:00:00Z\"", html);
            Assert.DoesNotContain("Internal route", html);
        }

        using var coordinatorClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(coordinatorClient, Coordinator);
        foreach (var path in new[]
        {
            "/Coordinator/Schedule",
            "/Coordinator/Requests",
            "/Coordinator/Coverage",
            $"/Coordinator/Assignments/Assign/{slotId}",
            $"/Coordinator/Assignments/Links/{assignmentId}"
        })
        {
            var html = await ReadAsync(coordinatorClient, path);
            Assert.Contains("Duration", html);
            Assert.Contains("Use the north entrance.", html);
        }

        var scheduleHtml = await ReadAsync(coordinatorClient, "/Coordinator/Schedule");
        Assert.Contains("Internal coordinator notes", scheduleHtml);
        Assert.Contains("Internal route", scheduleHtml);
    }

    private static VolunteerCoordinatorService CreateService(
        VolunteerCoordinatorDbContext context,
        IClock clock)
    {
        return new VolunteerCoordinatorService(
            new EfWorkflowStore(context),
            clock,
            new SecureTokenService(),
            new UnavailableNotificationService(context, clock));
    }

    private static async Task<string> ReadAsync(HttpClient client, string path) =>
        await (await client.GetAsync(path)).Content.ReadAsStringAsync();

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The page did not contain an antiforgery token.");
        return match.Groups[1].Value;
    }

    private static async Task SignInAsync(HttpClient client, string email)
    {
        var login = await client.GetAsync("/development/login");
        var token = ExtractAntiforgeryToken(await login.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/development/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["email"] = email
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
