using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Time;
using VolunteerCoordinator.Infrastructure.Security;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue17CoordinatorWebIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private readonly PostgreSqlFixture _fixture;

    public Issue17CoordinatorWebIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DevelopmentLoginLeadsToCoordinatorHomeAndShowsOrderedSetup()
    {
        await _fixture.ResetAsync();
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var login = await SignInAsync(client);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/Coordinator", login.Headers.Location?.OriginalString);

        var home = await client.GetStringAsync("/Coordinator");
        Assert.Contains("<h1 tabindex=\"-1\">Set up your schedule</h1>", home);
        Assert.Contains("Set up your schedule", home);
        Assert.Contains("Set your group time zone", home);
        Assert.Contains("Understand volunteer requests", home);
        Assert.Contains("Create the first schedule entry", home);
        Assert.Contains("Review what volunteers will see", home);
        Assert.Contains("Publish open commitments", home);
        Assert.Contains("Volunteers request a commitment. A coordinator reviews each request before anyone is assigned.", home);
        Assert.Contains("href=\"/Coordinator/Settings\"", home);
    }

    [Fact]
    public async Task LoginPageUsesHeadingAsDefaultFocusTarget()
    {
        await _fixture.ResetAsync();
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Account/Login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1 tabindex=\"-1\">Coordinator sign in</h1>", html);
        Assert.Contains("main h1[tabindex=\"-1\"]", html);
        Assert.Contains("if (window.location.hash)", html);
    }

    [Fact]
    public async Task PublicationReviewBackPathDoesNotMutateAuthoritativeState()
    {
        await _fixture.ResetAsync();
        await using (var context = _fixture.CreateContext())
        {
            var service = new VolunteerCoordinatorService(
                new EfWorkflowStore(context),
                new SystemClock(),
                new SecureTokenService(),
                new UnavailableNotificationService(context, new SystemClock()));
            await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Review before publication",
                "Community hall",
                "Use the north door.",
                DateTimeOffset.UtcNow.AddDays(1),
                DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
                1,
                Coordinator);
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await SignInAsync(client);

        var before = await ReadCountsAsync();
        await using (var context = _fixture.CreateContext())
        {
            var shiftId = await context.Shifts.Select(x => x.Id).SingleAsync();
            var review = await client.GetAsync($"/Coordinator/Schedule/Publish/{shiftId}");
            var html = await review.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);
            Assert.Contains("data-focus-target=\"true\"", html);
            Assert.Contains("Go back without changes", html);
            Assert.Contains("Publish commitments", html);
        }

        var after = await ReadCountsAsync();
        Assert.Equal(before, after);
    }
    [Fact]
    public async Task FirstPublicationConfirmationReturnsCoordinatorHome()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        Guid shiftId;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "First publication",
                null,
                null,
                clock.UtcNow.AddDays(1),
                clock.UtcNow.AddDays(1).AddHours(1),
                0,
                Coordinator,
                cancellationToken: default);
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client);
        var review = await client.GetStringAsync($"/Coordinator/Schedule/Publish/{shiftId}");
        var response = await client.PostAsync(
            $"/Coordinator/Schedule/Publish/{shiftId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractHiddenValue(review, "__RequestVerificationToken"),
                ["id"] = shiftId.ToString(),
                ["ExpectedVersion"] = ExtractHiddenValue(review, "ExpectedVersion"),
                ["ExpectedAffectedSet"] = ExtractHiddenValue(review, "ExpectedAffectedSet"),
                ["ExpectedSettingsVersion"] = ExtractHiddenValue(review, "ExpectedSettingsVersion")
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Coordinator", response.Headers.Location?.OriginalString);
        Assert.NotNull(response.Headers.Location);
        var home = await client.GetStringAsync(response.Headers.Location!.OriginalString);
        Assert.Contains("<h1 tabindex=\"-1\">", home);
    }

    [Fact]
    public async Task DisappearingPublicationTargetRendersStaleRecovery()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        Guid shiftId;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Disappearing publication",
                null,
                null,
                clock.UtcNow.AddDays(1),
                clock.UtcNow.AddDays(1).AddHours(1),
                0,
                Coordinator,
                cancellationToken: default);
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client);
        var review = await client.GetStringAsync($"/Coordinator/Schedule/Publish/{shiftId}");
        await using (var deletionContext = _fixture.CreateContext())
        {
            var shift = await deletionContext.Shifts.SingleAsync(x => x.Id == shiftId);
            deletionContext.Shifts.Remove(shift);
            await deletionContext.SaveChangesAsync();
        }

        var response = await client.PostAsync(
            $"/Coordinator/Schedule/Publish/{shiftId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractHiddenValue(review, "__RequestVerificationToken"),
                ["id"] = shiftId.ToString(),
                ["ExpectedVersion"] = ExtractHiddenValue(review, "ExpectedVersion"),
                ["ExpectedAffectedSet"] = ExtractHiddenValue(review, "ExpectedAffectedSet"),
                ["ExpectedSettingsVersion"] = ExtractHiddenValue(review, "ExpectedSettingsVersion")
            }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("This review is no longer current", html);
        Assert.Contains(VolunteerCoordinatorService.StalePreviewMessage, html);
        Assert.Contains("data-focus-target=\"true\"", html);
        Assert.Contains("Return without changes", html);
        Assert.DoesNotContain("Publish commitments", html);
    }

    [Fact]
    public async Task AssignmentReviewEditPreservesAllDetailsWithoutContactQueryValues()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        Guid slotId;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Assignment detail preservation",
                null,
                null,
                clock.UtcNow.AddDays(1),
                clock.UtcNow.AddDays(1).AddHours(1),
                0,
                Coordinator,
                cancellationToken: default);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client);
        var formPage = await client.GetStringAsync($"/Coordinator/Assignments/Assign/{slotId}?mode=new");
        var reviewResponse = await client.PostAsync(
            $"/Coordinator/Assignments/Assign/{slotId}?handler=Review",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractHiddenValue(formPage, "__RequestVerificationToken"),
                ["Mode"] = "new",
                ["VolunteerName"] = "Preserved detail",
                ["VolunteerEmail"] = "preserved@example.org",
                ["VolunteerPhone"] = "555-0144"
            }));
        var reviewHtml = await reviewResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        Assert.Contains("Change selection or details", reviewHtml);
        var hiddenInputHtml = string.Join(
            Environment.NewLine,
            Regex.Matches(reviewHtml, "<input[^>]*type=\"hidden\"[^>]*>")
                .Select(match => match.Value));
        Assert.Contains("name=\"ReviewToken\"", hiddenInputHtml);
        Assert.DoesNotContain("Preserved detail", hiddenInputHtml);
        Assert.DoesNotContain("preserved@example.org", hiddenInputHtml);
        Assert.DoesNotContain("555-0144", hiddenInputHtml);

        var editResponse = await client.PostAsync(
            $"/Coordinator/Assignments/Assign/{slotId}?handler=Edit",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractHiddenValue(reviewHtml, "__RequestVerificationToken"),
                ["ReviewToken"] = ExtractHiddenValue(reviewHtml, "ReviewToken"),
                ["Mode"] = "new",
                ["VolunteerName"] = "Preserved detail",
                ["VolunteerEmail"] = "preserved@example.org",
                ["VolunteerPhone"] = "555-0144"
            }));
        var editHtml = await editResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        Assert.Contains("name=\"VolunteerName\" value=\"Preserved detail\"", editHtml);
        Assert.Contains("name=\"VolunteerEmail\" value=\"preserved@example.org\"", editHtml);
        Assert.Contains("name=\"VolunteerPhone\" value=\"555-0144\"", editHtml);
        Assert.DoesNotContain("preserved@example.org", editResponse.RequestMessage?.RequestUri?.Query);
    }

    [Fact]
    public async Task TamperedAssignmentReviewStateCannotConfirm()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        Guid slotId;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Tampered review",
                null,
                null,
                clock.UtcNow.AddDays(1),
                clock.UtcNow.AddDays(1).AddHours(1),
                0,
                Coordinator,
                cancellationToken: default);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client);
        var formPage = await client.GetStringAsync($"/Coordinator/Assignments/Assign/{slotId}?mode=new");
        var reviewResponse = await client.PostAsync(
            $"/Coordinator/Assignments/Assign/{slotId}?handler=Review",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractHiddenValue(formPage, "__RequestVerificationToken"),
                ["Mode"] = "new",
                ["VolunteerName"] = "Tampered volunteer",
                ["VolunteerEmail"] = "tampered@example.org"
            }));
        var reviewHtml = await reviewResponse.Content.ReadAsStringAsync();
        var token = ExtractHiddenValue(reviewHtml, "ReviewToken");
        var tamperedToken = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        var confirmResponse = await client.PostAsync(
            $"/Coordinator/Assignments/Assign/{slotId}?handler=Confirm",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractHiddenValue(reviewHtml, "__RequestVerificationToken"),
                ["ReviewToken"] = tamperedToken
            }));
        var confirmHtml = await confirmResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.Contains("This review is no longer current", confirmHtml);
        await using var verificationContext = _fixture.CreateContext();
        Assert.Empty(await verificationContext.Assignments.ToListAsync());
    }

    [Fact]
    public async Task ValidationResponseProvidesDeterministicFocusTarget()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        Guid slotId;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Keyboard focus validation",
                null,
                null,
                clock.UtcNow.AddDays(1),
                clock.UtcNow.AddDays(1).AddHours(1),
                0,
                Coordinator,
                cancellationToken: default);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client);
        var formPage = await client.GetStringAsync($"/Coordinator/Assignments/Assign/{slotId}?mode=known");
        var response = await client.PostAsync(
            $"/Coordinator/Assignments/Assign/{slotId}?handler=Review",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractHiddenValue(formPage, "__RequestVerificationToken"),
                ["Mode"] = "known"
            }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-focus-target=\"true\"", html);
        Assert.Contains("DOMContentLoaded", html);
        Assert.Contains("main h1[tabindex=\"-1\"]", html);
    }

    [Fact]
    public async Task OrdinaryCoordinatorNavigationUsesPageHeadingFocusTarget()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            await ScheduleTestHelpers.EnsureSettingsAsync(service, Coordinator);
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client);

        var response = await client.GetAsync("/Coordinator/Schedule");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1 tabindex=\"-1\">Schedule</h1>", html);
        Assert.Contains("main h1[tabindex=\"-1\"]", html);
    }

    [Fact]
    public async Task MessagesPageShowsPrivacyAppropriateDirectFollowUp()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Message follow-up",
                null,
                null,
                clock.UtcNow.AddDays(1),
                clock.UtcNow.AddDays(1).AddHours(1),
                0,
                Coordinator,
                cancellationToken: default);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            await service.SubmitRequestAsync(
                shift.Slots.Single().Id,
                "Follow-up volunteer",
                "follow-up@example.org",
                "555-0188",
                default);
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client);
        var homeHtml = await client.GetStringAsync("/Coordinator");
        var filteredHtml = await client.GetStringAsync("/Coordinator/Messages?attention=message");
        var unknownFilterHtml = await client.GetStringAsync("/Coordinator/Messages?attention=not-allowed");

        Assert.Contains("href=\"/Coordinator/Messages?attention=message\"", homeHtml);
        Assert.Contains("Showing: Messages not sent", filteredHtml);
        Assert.DoesNotContain("Showing: Messages not sent", unknownFilterHtml);
        Assert.Contains("Showing 1–1 of 1 messages needing follow-up.", filteredHtml);
        Assert.Contains("mailto:follow-up@example.org", filteredHtml);
        Assert.Contains("tel:555-0188", filteredHtml);
        Assert.Contains("Message could not be sent. Contact the volunteer another way.", filteredHtml);
    }

    private static string ExtractHiddenValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"");
        Assert.True(match.Success, $"The page did not contain hidden field '{name}'.");
        return match.Groups[1].Value;
    }

    private async Task<PersistenceCounts> ReadCountsAsync()
    {
        await using var context = _fixture.CreateContext();
        return new PersistenceCounts(
            await context.Shifts.CountAsync(x => x.PublishedAtUtc.HasValue),
            await context.AuditEntries.CountAsync());
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client)
    {
        string loginToken = string.Empty;
        for (var attempt = 0; attempt < 20 && string.IsNullOrEmpty(loginToken); attempt++)
        {
            var loginForm = await client.GetAsync("/development/login");
            var loginHtml = await loginForm.Content.ReadAsStringAsync();
            loginToken = Regex.Match(
                loginHtml,
                "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            if (string.IsNullOrEmpty(loginToken))
            {
                await Task.Delay(100);
            }
        }

        Assert.NotEmpty(loginToken);
        return await client.PostAsync(
            "/development/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = loginToken,
                ["email"] = Coordinator
            }));
    }

    private sealed record PersistenceCounts(int PublishedShifts, int AuditEntries);
}
