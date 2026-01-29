using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Tests.Unit;

public class ShiftRequestTests
{
    private VsmsDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<VsmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new VsmsDbContext(options);
    }

    private async Task<(VsmsDbContext context, Shift shift, Volunteer volunteer)> SetupTestData()
    {
        var context = CreateInMemoryContext();

        var volunteer = new Volunteer
        {
            Name = "Test Volunteer",
            Email = "test@example.com",
            IsActive = true
        };
        context.Volunteers.Add(volunteer);

        var timeSlot = new TimeSlot
        {
            Label = "Morning",
            StartTime = new TimeOnly(9, 0),
            DurationMinutes = 180,
            IsActive = true,
            SortOrder = 1
        };
        context.TimeSlots.Add(timeSlot);
        await context.SaveChangesAsync();

        var shift = new Shift
        {
            Date = new DateOnly(2026, 2, 15),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            Status = ShiftStatus.Open
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        return (context, shift, volunteer);
    }

    [Fact]
    public async Task ShiftRequest_DefaultStatus_IsPending()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();

        // Act
        var request = new ShiftRequest
        {
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            RequestedAt = DateTime.UtcNow
        };
        context.ShiftRequests.Add(request);
        await context.SaveChangesAsync();

        // Assert
        var saved = await context.ShiftRequests.FindAsync(request.Id);
        Assert.NotNull(saved);
        Assert.Equal(RequestStatus.Pending, saved.Status);
        Assert.Null(saved.ResolvedAt);
    }

    [Fact]
    public async Task ShiftRequest_Approved_UpdatesShiftAndRequest()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();
        var admin = new AdminUser
        {
            GoogleId = "admin-123",
            Email = "admin@example.com",
            Name = "Admin User",
            CreatedAt = DateTime.UtcNow
        };
        context.AdminUsers.Add(admin);

        var request = new ShiftRequest
        {
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            RequestedAt = DateTime.UtcNow
        };
        context.ShiftRequests.Add(request);
        await context.SaveChangesAsync();

        // Act - Simulate approval
        request.Status = RequestStatus.Approved;
        request.ResolvedAt = DateTime.UtcNow;
        request.ResolvedByAdminId = admin.Id;

        shift.VolunteerId = volunteer.Id;
        shift.Status = ShiftStatus.Assigned;
        shift.AssignedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        // Assert
        var savedRequest = await context.ShiftRequests.FindAsync(request.Id);
        var savedShift = await context.Shifts.FindAsync(shift.Id);

        Assert.NotNull(savedRequest);
        Assert.NotNull(savedShift);

        Assert.Equal(RequestStatus.Approved, savedRequest.Status);
        Assert.NotNull(savedRequest.ResolvedAt);
        Assert.Equal(admin.Id, savedRequest.ResolvedByAdminId);

        Assert.Equal(ShiftStatus.Assigned, savedShift.Status);
        Assert.Equal(volunteer.Id, savedShift.VolunteerId);
    }

    [Fact]
    public async Task ShiftRequest_Rejected_ShiftRemainsOpen()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();
        var admin = new AdminUser
        {
            GoogleId = "admin-456",
            Email = "admin2@example.com",
            Name = "Admin Two",
            CreatedAt = DateTime.UtcNow
        };
        context.AdminUsers.Add(admin);

        var request = new ShiftRequest
        {
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            RequestedAt = DateTime.UtcNow
        };
        context.ShiftRequests.Add(request);
        await context.SaveChangesAsync();

        // Act - Simulate rejection
        request.Status = RequestStatus.Rejected;
        request.ResolvedAt = DateTime.UtcNow;
        request.ResolvedByAdminId = admin.Id;
        await context.SaveChangesAsync();

        // Assert
        var savedRequest = await context.ShiftRequests.FindAsync(request.Id);
        var savedShift = await context.Shifts.FindAsync(shift.Id);

        Assert.NotNull(savedRequest);
        Assert.NotNull(savedShift);

        Assert.Equal(RequestStatus.Rejected, savedRequest.Status);
        Assert.Equal(ShiftStatus.Open, savedShift.Status);
        Assert.Null(savedShift.VolunteerId);
    }

    [Fact]
    public async Task MultipleRequests_SameShift_AllTracked()
    {
        // Arrange
        var (context, shift, volunteer1) = await SetupTestData();
        var volunteer2 = new Volunteer
        {
            Name = "Second Volunteer",
            Email = "second@example.com",
            IsActive = true
        };
        context.Volunteers.Add(volunteer2);
        await context.SaveChangesAsync();

        // Act
        var request1 = new ShiftRequest
        {
            ShiftId = shift.Id,
            VolunteerId = volunteer1.Id,
            RequestedAt = DateTime.UtcNow
        };
        var request2 = new ShiftRequest
        {
            ShiftId = shift.Id,
            VolunteerId = volunteer2.Id,
            RequestedAt = DateTime.UtcNow.AddMinutes(5)
        };
        context.ShiftRequests.AddRange(request1, request2);
        await context.SaveChangesAsync();

        // Assert
        var requests = await context.ShiftRequests
            .Where(r => r.ShiftId == shift.Id)
            .ToListAsync();

        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Equal(RequestStatus.Pending, r.Status));
    }

    [Fact]
    public async Task ShiftRequest_CanLoadRelatedData()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();

        var request = new ShiftRequest
        {
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            RequestedAt = DateTime.UtcNow
        };
        context.ShiftRequests.Add(request);
        await context.SaveChangesAsync();

        // Act
        var loaded = await context.ShiftRequests
            .Include(r => r.Shift)
                .ThenInclude(s => s.TimeSlot)
            .Include(r => r.Volunteer)
            .FirstAsync(r => r.Id == request.Id);

        // Assert
        Assert.NotNull(loaded.Shift);
        Assert.NotNull(loaded.Shift.TimeSlot);
        Assert.NotNull(loaded.Volunteer);
        Assert.Equal("Morning", loaded.Shift.TimeSlot.Label);
        Assert.Equal("Test Volunteer", loaded.Volunteer.Name);
    }
}
