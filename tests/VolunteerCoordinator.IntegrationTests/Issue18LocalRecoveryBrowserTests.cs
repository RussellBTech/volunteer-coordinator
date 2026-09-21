using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue18LocalRecoveryBrowserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlFixture _fixture;

    public Issue18LocalRecoveryBrowserTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FakeDeliveryRecoveryTokenRedeemsThroughLocalHttpFlow()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        Guid slotId;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Recovery browser flow",
                "Local hall",
                null,
                Now.AddDays(3),
                Now.AddDays(3).AddHours(2),
                0,
                "coordinator@example.org");
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, "coordinator@example.org", default);
            slotId = shift.Slots.Single().Id;
        }

        var fake = new FakeTransactionalEmailProvider();
        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: clock,
            emailProvider: fake);
        await using var flowScope = factory.Services.CreateAsyncScope();
        var flowService = flowScope.ServiceProvider.GetRequiredService<VolunteerCoordinatorService>();
        var oldHub = (await flowService.SubmitRequestAsync(
            slotId,
            "Recovery volunteer",
            "recover-browser@example.org",
            null,
            default)).StatusToken;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        HttpResponseMessage formResponse = await client.GetAsync("/Commitments/Recover");
        for (var attempt = 0; attempt < 30 && formResponse.StatusCode == HttpStatusCode.ServiceUnavailable; attempt++)
        {
            formResponse.Dispose();
            await Task.Delay(100);
            formResponse = await client.GetAsync("/Commitments/Recover");
        }
        var formHtml = await formResponse.Content.ReadAsStringAsync();
        var antiforgeryMatch = Regex.Match(
            formHtml,
            "(?:name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\")");
        var antiforgery = antiforgeryMatch.Groups[1].Success
            ? antiforgeryMatch.Groups[1].Value
            : antiforgeryMatch.Groups[2].Value;
        Assert.NotEmpty(antiforgery);
        var receipt = await client.PostAsync(
            "/Commitments/Recover",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgery,
                ["Email"] = "recover-browser@example.org",
                ["CommitmentDate"] = "2026-09-04"
            }));
        Assert.Equal(HttpStatusCode.Redirect, receipt.StatusCode);

        var worker = factory.Services
            .GetServices<IHostedService>()
            .OfType<NotificationDeliveryHostedService>()
            .Single();
        await worker.RunOnceAsync(default);

        var recoveryMessage = Assert.Single(
            fake.Messages,
            x => x.Template.TextBody.Contains(
                "/Commitments/Recover/",
                StringComparison.Ordinal));
        Assert.DoesNotContain("recover-browser@example.org", recoveryMessage.Template.TextBody, StringComparison.Ordinal);
        var recoveryUrl = Regex.Match(
            recoveryMessage.Template.TextBody,
            "https://localhost/Commitments/Recover/([^\\s]+)").Value;
        Assert.NotEmpty(recoveryUrl);
        var recoveryPath = new Uri(recoveryUrl).PathAndQuery;

        var redemption = await client.GetAsync(recoveryPath);
        Assert.Equal(HttpStatusCode.Redirect, redemption.StatusCode);
        var hubPath = redemption.Headers.Location?.OriginalString;
        Assert.StartsWith("/Requests/Status/", hubPath, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(hubPath)).StatusCode);

        var replay = await client.GetAsync(recoveryPath);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Contains("invalid or has expired", await replay.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var oldHubResponse = await client.GetAsync($"/Requests/Status/{Uri.EscapeDataString(oldHub)}");
        Assert.Equal(HttpStatusCode.OK, oldHubResponse.StatusCode);
        Assert.Contains("invalid or has expired", await oldHubResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var verification = _fixture.CreateContext();
        var recoveryTokens = await verification.RecoveryTokens.ToListAsync();
        Assert.Single(recoveryTokens);
        Assert.NotNull(recoveryTokens[0].UsedAtUtc);
        Assert.Equal(2, await verification.VolunteerAccessCapabilities.CountAsync());
    }

    [Fact]
    public async Task RestartedDeliveryUsesScopedRecoveryWithoutReplacingBrowserHub()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        Guid slotId;
        string browserHub;
        await using (var setupContext = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(setupContext, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Restart recovery",
                null,
                null,
                Now.AddDays(2),
                Now.AddDays(2).AddHours(1),
                0,
                "coordinator@example.org");
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, "coordinator@example.org", default);
            slotId = shift.Slots.Single().Id;
            browserHub = (await service.SubmitRequestAsync(
                slotId,
                "Restart volunteer",
                "restart@example.org",
                null,
                default)).StatusToken;
        }

        var fake = new FakeTransactionalEmailProvider();
        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: clock,
            emailProvider: fake);
        var worker = factory.Services
            .GetServices<IHostedService>()
            .OfType<NotificationDeliveryHostedService>()
            .Single();
        await worker.RunOnceAsync(default);

        var replacement = Assert.Single(
            fake.Messages,
            message => message.Template.TextBody.Contains(
                "/Commitments/Recover/",
                StringComparison.Ordinal));
        using var client = factory.CreateClient();
        var browserHubResponse = await client.GetAsync(
            $"/Requests/Status/{Uri.EscapeDataString(browserHub)}");
        Assert.Equal(HttpStatusCode.OK, browserHubResponse.StatusCode);
        Assert.DoesNotContain("invalid or has expired", await browserHubResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("/Commitments/Recover/", replacement.Template.TextBody, StringComparison.Ordinal);
    }
}
