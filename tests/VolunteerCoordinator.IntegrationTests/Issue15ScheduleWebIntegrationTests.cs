using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Infrastructure.Persistence;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue15ScheduleWebIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlFixture _fixture;

    public Issue15ScheduleWebIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LocalControlsRejectGapAndRequireThenAcceptOverlapChoice()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client);

        var settingsPage = await client.GetAsync("/Coordinator/Settings");
        var settingsToken = ExtractAntiforgeryToken(await settingsPage.Content.ReadAsStringAsync());
        var configured = await client.PostAsync(
            "/Coordinator/Settings",
            Form(
                settingsToken,
                ("TimeZoneId", "America/New_York"),
                ("ExpectedVersion", "")));
        Assert.Equal(HttpStatusCode.Redirect, configured.StatusCode);

        uint settingsVersion;
        await using (var context = _fixture.CreateContext())
        {
            settingsVersion = await context.GroupSettings.Select(x => x.Version).SingleAsync();
        }

        var ordinaryForm = await client.GetAsync("/Coordinator/Schedule/Create");
        var ordinaryToken = ExtractAntiforgeryToken(await ordinaryForm.Content.ReadAsStringAsync());
        var ordinary = await client.PostAsync(
            "/Coordinator/Schedule/Create",
            Form(
                ordinaryToken,
                ("Title", "Ordinary local shift"),
                ("Location", "Hall"),
                ("VolunteerInstructions", "Use the side door."),
                ("Notes", "Private route"),
                ("StartsAtLocal", "2026-10-03T10:00"),
                ("EndsAtLocal", "2026-10-03T12:00"),
                ("StartsAtOffset", ""),
                ("EndsAtOffset", ""),
                ("BackupSlotCount", "0"),
                ("ExpectedSettingsVersion", settingsVersion.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, ordinary.StatusCode);

        var overlapForm = await client.GetAsync("/Coordinator/Schedule/Create");
        var overlapToken = ExtractAntiforgeryToken(await overlapForm.Content.ReadAsStringAsync());
        var unresolved = await client.PostAsync(
            "/Coordinator/Schedule/Create",
            Form(
                overlapToken,
                ("Title", "Repeated local shift"),
                ("Location", "Hall"),
                ("VolunteerInstructions", "Choose the marked entrance."),
                ("Notes", "Private overlap"),
                ("StartsAtLocal", "2026-11-01T01:30"),
                ("EndsAtLocal", "2026-11-01T02:30"),
                ("StartsAtOffset", ""),
                ("EndsAtOffset", ""),
                ("BackupSlotCount", "0"),
                ("ExpectedSettingsVersion", settingsVersion.ToString())));
        var unresolvedHtml = await unresolved.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, unresolved.StatusCode);
        Assert.Contains("Choose the start interpretation", unresolvedHtml);
        Assert.Contains("EDT (UTC-04:00)", unresolvedHtml);
        Assert.Contains("EST (UTC-05:00)", unresolvedHtml);

        await using (var context = _fixture.CreateContext())
        {
            Assert.Single(await context.Shifts.ToListAsync());
        }

        var selectedFormToken = ExtractAntiforgeryToken(unresolvedHtml);
        var resolved = await client.PostAsync(
            "/Coordinator/Schedule/Create",
            Form(
                selectedFormToken,
                ("Title", "Repeated local shift"),
                ("Location", "Hall"),
                ("VolunteerInstructions", "Choose the marked entrance."),
                ("Notes", "Private overlap"),
                ("StartsAtLocal", "2026-11-01T01:30"),
                ("EndsAtLocal", "2026-11-01T02:30"),
                ("StartsAtOffset", "-05:00"),
                ("EndsAtOffset", ""),
                ("BackupSlotCount", "0"),
                ("ExpectedSettingsVersion", settingsVersion.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, resolved.StatusCode);

        var gapForm = await client.GetAsync("/Coordinator/Schedule/Create");
        var gapToken = ExtractAntiforgeryToken(await gapForm.Content.ReadAsStringAsync());
        var gap = await client.PostAsync(
            "/Coordinator/Schedule/Create",
            Form(
                gapToken,
                ("Title", "Gap local shift"),
                ("Location", "Hall"),
                ("VolunteerInstructions", "Not saved."),
                ("Notes", "Not saved."),
                ("StartsAtLocal", "2026-03-08T02:30"),
                ("EndsAtLocal", "2026-03-08T04:00"),
                ("StartsAtOffset", ""),
                ("EndsAtOffset", ""),
                ("BackupSlotCount", "0"),
                ("ExpectedSettingsVersion", settingsVersion.ToString())));
        Assert.Equal(HttpStatusCode.OK, gap.StatusCode);
        Assert.Contains("This local time does not exist because the clocks move forward. Choose another time.", await gap.Content.ReadAsStringAsync());

        await using var verification = _fixture.CreateContext();
        var shifts = await verification.Shifts.ToListAsync();
        Assert.Equal(2, shifts.Count);
        var overlap = shifts.Single(x => x.Title == "Repeated local shift");
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), overlap.StartsAtUtc);
    }

    private static FormUrlEncodedContent Form(string antiforgeryToken, params (string Key, string Value)[] values)
    {
        var form = values.ToDictionary(pair => pair.Key, pair => pair.Value);
        form["__RequestVerificationToken"] = antiforgeryToken;
        return new FormUrlEncodedContent(form);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The page did not contain an antiforgery token.");
        return match.Groups[1].Value;
    }

    private static async Task SignInAsync(HttpClient client)
    {
        var login = await client.GetAsync("/development/login");
        var token = ExtractAntiforgeryToken(await login.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/development/login",
            Form(token, ("email", Coordinator)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
