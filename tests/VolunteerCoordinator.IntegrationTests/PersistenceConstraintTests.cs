using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Volunteers;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class PersistenceConstraintTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlFixture _fixture;

    public PersistenceConstraintTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MigrationPersistsUtcScheduleAndSlots()
    {
        await _fixture.ResetAsync();
        var shift = Shift.Create("Food service", "Community hall", null, Now.AddHours(1), Now.AddHours(2), 2);
        await using (var context = _fixture.CreateContext())
        {
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();
        }

        await using var verification = _fixture.CreateContext();
        var persisted = await verification.Shifts.Include(x => x.Slots).SingleAsync();
        Assert.Equal(TimeSpan.Zero, persisted.StartsAtUtc.Offset);
        Assert.Equal(3, persisted.Slots.Count);
        Assert.True(persisted.Version > 0);
    }

    [Fact]
    public async Task PostgreSqlRejectsDuplicatePendingRequest()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var shift = Shift.Create("Food service", null, null, Now.AddHours(1), Now.AddHours(2), 0);
        var volunteer = Volunteer.Create("Alex", "alex@example.org", null, Now);
        context.AddRange(shift, volunteer);
        await context.SaveChangesAsync();
        var slotId = shift.Slots.Single().Id;
        context.ShiftRequests.Add(ShiftRequest.Create(slotId, volunteer.Id, Enumerable.Repeat((byte)1, 32).ToArray(), Now, Now.AddDays(30)));
        await context.SaveChangesAsync();
        context.ShiftRequests.Add(ShiftRequest.Create(slotId, volunteer.Id, Enumerable.Repeat((byte)2, 32).ToArray(), Now, Now.AddDays(30)));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task PostgreSqlRejectsSecondActiveAssignmentForSlot()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var shift = Shift.Create("Food service", null, null, Now.AddHours(1), Now.AddHours(2), 0);
        var firstVolunteer = Volunteer.Create("Alex", "alex@example.org", null, Now);
        var secondVolunteer = Volunteer.Create("Blair", "blair@example.org", null, Now);
        context.AddRange(shift, firstVolunteer, secondVolunteer);
        await context.SaveChangesAsync();
        var slotId = shift.Slots.Single().Id;
        context.Assignments.Add(Assignment.Create(slotId, shift.Id, firstVolunteer.Id, null, "COORDINATOR@EXAMPLE.ORG", Now));
        await context.SaveChangesAsync();
        context.Assignments.Add(Assignment.Create(slotId, shift.Id, secondVolunteer.Id, null, "COORDINATOR@EXAMPLE.ORG", Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task PostgreSqlRejectsSecondUnusedTokenForAssignmentAction()
    {
        await _fixture.ResetAsync();
        var seeded = await SeedConcurrencyRowsAsync();
        await using var context = _fixture.CreateContext();
        context.ActionTokens.Add(ActionToken.Create(
            seeded.AssignmentId,
            VolunteerAction.Confirm,
            Enumerable.Repeat((byte)9, 32).ToArray(),
            Now,
            Now.AddDays(7)));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentActionTokenConsumptionRejectsSecondSave()
    {
        await _fixture.ResetAsync();
        var seeded = await SeedConcurrencyRowsAsync();
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var first = await firstContext.ActionTokens.SingleAsync(x => x.Id == seeded.ActionTokenId);
        var second = await secondContext.ActionTokens.SingleAsync(x => x.Id == seeded.ActionTokenId);
        first.Consume(Now.AddMinutes(1));
        second.Consume(Now.AddMinutes(1));

        await firstContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentAssignmentTransitionsRejectSecondSave()
    {
        await _fixture.ResetAsync();
        var seeded = await SeedConcurrencyRowsAsync();
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var first = await firstContext.Assignments.SingleAsync(x => x.Id == seeded.AssignmentId);
        var second = await secondContext.Assignments.SingleAsync(x => x.Id == seeded.AssignmentId);
        first.Confirm(Now.AddMinutes(1));
        second.Decline(Now.AddMinutes(1));

        await firstContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentRequestResolutionsRejectSecondSave()
    {
        await _fixture.ResetAsync();
        var seeded = await SeedConcurrencyRowsAsync();
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var first = await firstContext.ShiftRequests.SingleAsync(x => x.Id == seeded.RequestId);
        var second = await secondContext.ShiftRequests.SingleAsync(x => x.Id == seeded.RequestId);
        first.Approve("coordinator@example.org", Now.AddMinutes(1));
        second.Reject("coordinator@example.org", Now.AddMinutes(1));

        await firstContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    private async Task<(Guid AssignmentId, Guid RequestId, Guid ActionTokenId)> SeedConcurrencyRowsAsync()
    {
        await using var context = _fixture.CreateContext();
        var shift = Shift.Create("Food service", null, null, Now.AddHours(1), Now.AddHours(2), 0);
        var volunteer = Volunteer.Create("Alex", "alex@example.org", null, Now);
        context.AddRange(shift, volunteer);
        await context.SaveChangesAsync();
        var slotId = shift.Slots.Single().Id;
        var request = ShiftRequest.Create(
            slotId,
            volunteer.Id,
            Enumerable.Repeat((byte)1, 32).ToArray(),
            Now,
            Now.AddDays(30));
        var assignment = Assignment.Create(
            slotId,
            shift.Id,
            volunteer.Id,
            null,
            "COORDINATOR@EXAMPLE.ORG",
            Now);
        var actionToken = ActionToken.Create(
            assignment.Id,
            VolunteerAction.Confirm,
            Enumerable.Repeat((byte)2, 32).ToArray(),
            Now,
            Now.AddDays(7));
        context.AddRange(request, assignment, actionToken);
        await context.SaveChangesAsync();
        return (assignment.Id, request.Id, actionToken.Id);
    }
}
