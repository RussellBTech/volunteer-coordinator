using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Volunteers;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue16PrivacyWebIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlFixture _fixture;

    public Issue16PrivacyWebIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PublicPrivacyAndContactFormsRenderApprovedNoticeBeforeSubmission()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        Guid slotId;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Public privacy shift",
                "Community hall",
                null,
                FixedNow.AddDays(2),
                FixedNow.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await WaitForReadyAsync(client);
        var privacyHtml = await ReadAsync(client, "/Privacy");
        var requestHtml = await ReadAsync(client, $"/Shifts/Request/{slotId}");

        Assert.Contains("service scheduling only", privacyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("365 elapsed UTC days", privacyHtml, StringComparison.Ordinal);
        Assert.Contains("privacy@example.invalid", privacyHtml, StringComparison.Ordinal);
        Assert.Contains("does not indicate AA membership or attendance", privacyHtml, StringComparison.Ordinal);
        Assert.Contains("Phone (optional)", requestHtml, StringComparison.Ordinal);
        Assert.Contains("Protected backups expire through their normal rotation", requestHtml, StringComparison.Ordinal);
        Assert.Contains("privacy@example.invalid", requestHtml, StringComparison.Ordinal);
        Assert.True(
            requestHtml.IndexOf("privacy-notice", StringComparison.Ordinal) <
            requestHtml.IndexOf("Submit request", StringComparison.Ordinal));
    }
    [Fact]
    public async Task InitialRetentionCompletesBeforeReadinessAndWebServing()
    {
        await _fixture.ResetAsync();
        Guid volunteerId;
        await using (var context = _fixture.CreateContext())
        {
            var volunteer = Volunteer.Create(
                "Retention worker",
                "retention-worker@example.org",
                null,
                FixedNow.AddDays(-400));
            context.Volunteers.Add(volunteer);
            await context.SaveChangesAsync();
            volunteerId = volunteer.Id;
        }

        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient();
        await WaitForReadyAsync(client);
        var servedPage = await client.GetAsync("/Privacy");
        Assert.Equal(HttpStatusCode.OK, servedPage.StatusCode);

        await using var verification = _fixture.CreateContext();
        var volunteerState = await verification.Volunteers.SingleAsync(x => x.Id == volunteerId);
        Assert.Equal(FixedNow, volunteerState.AnonymizedAtUtc);
        Assert.Equal(
            1,
            await verification.AuditEntries.CountAsync(x => x.Action == "VolunteerAnonymized"));
    }


    [Theory]
    [InlineData(365, 24, 100, true)]
    [InlineData(365, 24, 1, true)]
    [InlineData(364, 24, 100, false)]
    [InlineData(366, 24, 100, false)]
    [InlineData(365, 23, 100, false)]
    [InlineData(365, 25, 100, false)]
    [InlineData(365, 24, 0, false)]
    [InlineData(365, 24, 101, false)]
    public void RetentionOptionsRejectUnsafeBounds(
        int retentionDays,
        int intervalHours,
        int batchSize,
        bool expected)
    {
        var options = new VolunteerCoordinator.Web.Privacy.VolunteerRetentionOptions
        {
            RetentionDays = retentionDays,
            SweepIntervalHours = intervalHours,
            BatchSize = batchSize
        };

        Assert.Equal(expected, options.IsValid());
    }

    [Fact]
    public async Task CoordinatorMustMatchCommitmentAndCanRemoveEligibleContact()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var candidate = await SeedRemovalCandidateAsync(clock, pending: false);
        await using (var lookupContext = _fixture.CreateContext())
        {
            var directLookup = await ScheduleTestHelpers
                .CreateService(lookupContext, clock)
                .LookupVolunteerRemovalAsync(candidate.Email, default);
            Assert.NotNull(directLookup);
            Assert.NotEmpty(directLookup!.Commitments);
        }
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await WaitForReadyAsync(anonymous);
        var anonymousResponse = await anonymous.GetAsync("/Coordinator/Privacy");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.Equal("/Account/Login", anonymousResponse.Headers.Location?.AbsolutePath);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, Coordinator);
        var lookupPage = await client.GetAsync("/Coordinator/Privacy");
        var lookupHtml = await lookupPage.Content.ReadAsStringAsync();
        var lookupToken = ExtractAntiforgeryToken(lookupHtml);
        var lookupResponse = await client.PostAsync(
            "/Coordinator/Privacy?handler=Lookup",
            Form(
                ("__RequestVerificationToken", lookupToken),
                ("Email", candidate.Email)));
        Assert.Equal(HttpStatusCode.OK, lookupResponse.StatusCode);
        var matchedHtml = await lookupResponse.Content.ReadAsStringAsync();
        Assert.Contains("Choose the recent commitment supplied out of band", matchedHtml, StringComparison.Ordinal);
        Assert.Contains(candidate.ShiftId.ToString(), matchedHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(candidate.Email.ToUpperInvariant(), matchedHtml, StringComparison.Ordinal);
        Assert.Contains("name=\"NormalizedEmail\"", matchedHtml, StringComparison.Ordinal);
        var volunteerMatch = Regex.Match(
            matchedHtml,
            "(?:name=\"VolunteerId\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"VolunteerId\")");
        var volunteerId = volunteerMatch.Groups[1].Success
            ? volunteerMatch.Groups[1].Value
            : volunteerMatch.Groups[2].Value;
        Assert.Equal(candidate.VolunteerId.ToString(), volunteerId);
        var confirmToken = ExtractAntiforgeryToken(matchedHtml);
        var confirmResponse = await client.PostAsync(
            "/Coordinator/Privacy?handler=Confirm",
            Form(
                ("__RequestVerificationToken", confirmToken),
                ("NormalizedEmail", candidate.Email.ToUpperInvariant()),
                ("VolunteerId", candidate.VolunteerId.ToString()),
                ("SelectedShiftId", candidate.ShiftId.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, confirmResponse.StatusCode);
        var resultHtml = await ReadAsync(client, "/Coordinator/Privacy");
        Assert.Contains("Volunteer contact data removed. Non-identifying scheduling history was retained.", resultHtml, StringComparison.Ordinal);

        await using var verification = _fixture.CreateContext();
        var volunteer = await verification.Volunteers.SingleAsync(x => x.Id == candidate.VolunteerId);
        Assert.NotNull(volunteer.AnonymizedAtUtc);
        Assert.Single(await verification.AuditEntries.Where(x => x.Action == "VolunteerAnonymized").ToListAsync());
    }

    [Fact]
    public async Task CoordinatorRemovalExplainsLiveDependencyWithoutMutation()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var candidate = await SeedRemovalCandidateAsync(clock, pending: true);
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await WaitForReadyAsync(client);
        await SignInAsync(client, Coordinator);
        var lookupPage = await client.GetAsync("/Coordinator/Privacy");
        var lookupHtml = await lookupPage.Content.ReadAsStringAsync();
        var lookupToken = ExtractAntiforgeryToken(lookupHtml);
        var lookupResponse = await client.PostAsync(
            "/Coordinator/Privacy?handler=Lookup",
            Form(
                ("__RequestVerificationToken", lookupToken),
                ("Email", candidate.Email)));
        var matchedHtml = await lookupResponse.Content.ReadAsStringAsync();
        var confirmToken = ExtractAntiforgeryToken(matchedHtml);
        var response = await client.PostAsync(
            "/Coordinator/Privacy?handler=Confirm",
            Form(
                ("__RequestVerificationToken", confirmToken),
                ("NormalizedEmail", candidate.Email.ToUpperInvariant()),
                ("VolunteerId", candidate.VolunteerId.ToString()),
                ("SelectedShiftId", candidate.ShiftId.ToString())));
        var blockedHtml = await response.Content.ReadAsStringAsync();
        Assert.Contains("Removal is blocked by 1 pending request", blockedHtml, StringComparison.Ordinal);

        await using var verification = _fixture.CreateContext();
        var volunteer = await verification.Volunteers.SingleAsync(x => x.Id == candidate.VolunteerId);
        Assert.Null(volunteer.AnonymizedAtUtc);
        Assert.Empty(await verification.AuditEntries.Where(x => x.Action == "VolunteerAnonymized").ToListAsync());
    }

    private async Task<RemovalCandidate> SeedRemovalCandidateAsync(
        ScheduleTestHelpers.FixedClock clock,
        bool pending)
    {
        await using var context = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            pending ? "Pending removal commitment" : "Removal commitment",
            "Community hall",
            null,
            FixedNow.AddDays(-3),
            FixedNow.AddDays(-2),
            0,
            Coordinator);
        var shift = await context.Shifts.Include(x => x.Slots).SingleAsync(x => x.Id == shiftId);
        var volunteer = Volunteer.Create(
            pending ? "Pending volunteer" : "Removal volunteer",
            pending ? "pending-removal@example.org" : "removal@example.org",
            null,
            FixedNow.AddDays(-10));
        context.Volunteers.Add(volunteer);
        var tokenService = new SecureTokenService();
        var generated = tokenService.Generate();
        var request = ShiftRequest.Create(shift.Slots.Single().Id, volunteer.Id, FixedNow.AddDays(-3));
        if (!pending)
        {
            request.Approve(Coordinator, FixedNow.AddDays(-2));
        }

        context.ShiftRequests.Add(request);
        if (!pending)
        {
            var assignment = Assignment.Create(
                shift.Slots.Single().Id,
                shift.Id,
                volunteer.Id,
                request.Id,
                Coordinator,
                FixedNow.AddDays(-3));
            assignment.Reassign(FixedNow.AddDays(-2));
            context.Assignments.Add(assignment);
        }

        await context.SaveChangesAsync();
        return new RemovalCandidate(volunteer.Id, volunteer.Email, shift.Id);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.ToDictionary(x => x.Key, x => x.Value));

    private static async Task SignInAsync(HttpClient client, string email)
    {
        var login = await client.GetAsync("/development/login");
        var token = ExtractAntiforgeryToken(await login.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/development/login",
            Form(("__RequestVerificationToken", token), ("email", email)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<string> ReadAsync(HttpClient client, string path) =>
        await (await client.GetAsync(path)).Content.ReadAsStringAsync();
    private static async Task WaitForReadyAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var response = await client.GetAsync("/health/ready");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException("Web readiness did not become available.");
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The page did not contain an antiforgery token.");
        return match.Groups[1].Value;
    }

    private sealed record RemovalCandidate(Guid VolunteerId, string Email, Guid ShiftId);
}
