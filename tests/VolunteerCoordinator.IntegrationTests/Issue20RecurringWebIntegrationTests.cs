using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Infrastructure.Persistence;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue20RecurringWebIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private readonly PostgreSqlFixture _fixture;

    public Issue20RecurringWebIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RecurringCreatePreviewUsesLocalFieldsAndShowsConcreteRoles()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client);

        var settingsPage = await client.GetAsync("/Coordinator/Settings");
        var settingsToken = ExtractAntiforgeryToken(await settingsPage.Content.ReadAsStringAsync());
        var configured = await client.PostAsync(
            "/Coordinator/Settings",
            Form(
                ("__RequestVerificationToken", settingsToken),
                ("TimeZoneId", "Etc/UTC"),
                ("ExpectedVersion", "")));
        Assert.Equal(HttpStatusCode.Redirect, configured.StatusCode);

        await using var context = _fixture.CreateContext();
        var settingsVersion = await context.GroupSettings.Select(x => x.Version).SingleAsync();
        var createPage = await client.GetAsync("/Coordinator/Recurring/Create");
        Assert.Equal(HttpStatusCode.OK, createPage.StatusCode);
        var createHtml = await createPage.Content.ReadAsStringAsync();
        Assert.Contains("Preview dates", createHtml);
        Assert.Contains("Elapsed duration in minutes", createHtml);
        Assert.DoesNotContain("RRULE", createHtml, StringComparison.OrdinalIgnoreCase);
        var preview = await client.PostAsync(
            "/Coordinator/Recurring/Create?handler=Preview",
            Form(
                ("__RequestVerificationToken", ExtractAntiforgeryToken(createHtml)),
                ("handler", "Preview"),
                ("Title", "Weekly local meeting"),
                ("Location", "Hall"),
                ("VolunteerInstructions", "Use the side door."),
                ("InternalCoordinatorNotes", "Private note"),
                ("RecurrenceKind", "Weekly"),
                ("Interval", "1"),
                ("Weekdays", "Monday"),
                ("Weekdays", "Thursday"),
                ("AnchorLocalDate", "2026-01-05"),
                ("LocalStartTime", "09:00"),
                ("DurationMinutes", "120"),
                ("BackupSlotCount", "1"),
                ("HorizonWeeks", "4"),
                ("AmbiguousTimeChoice", "FirstOccurrence"),
                ("ExpectedSettingsVersion", settingsVersion.ToString())));
        var previewHtml = await preview.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("Preview of actual dates", previewHtml);
        Assert.Contains("Primary", previewHtml);
        Assert.Contains("Backup 1", previewHtml);
        Assert.Contains("Monday", previewHtml);
        Assert.Contains("Thursday", previewHtml);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Key, value.Value)));

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
            Form(
                ("__RequestVerificationToken", token),
                ("email", Coordinator)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
