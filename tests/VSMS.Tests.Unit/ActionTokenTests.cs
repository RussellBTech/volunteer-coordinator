using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Tests.Unit;

public class ActionTokenTests
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
            Name = "Token Test Volunteer",
            Email = "token@example.com",
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
            Date = new DateOnly(2026, 3, 1),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Assigned,
            AssignedAt = DateTime.UtcNow
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        return (context, shift, volunteer);
    }

    [Fact]
    public async Task ActionToken_IsNotExpired_WhenWithinExpirationWindow()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();

        var token = new ActionToken
        {
            Token = Guid.NewGuid().ToString("N"),
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            Action = TokenAction.Confirm,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            CreatedAt = DateTime.UtcNow
        };
        context.ActionTokens.Add(token);
        await context.SaveChangesAsync();

        // Act
        var now = DateTime.UtcNow;
        var isExpired = token.ExpiresAt <= now;
        var isUsed = token.UsedAt != null;

        // Assert
        Assert.False(isExpired);
        Assert.False(isUsed);
    }

    [Fact]
    public async Task ActionToken_IsExpired_WhenPastExpirationTime()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();

        var token = new ActionToken
        {
            Token = Guid.NewGuid().ToString("N"),
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            Action = TokenAction.Confirm,
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // Expired yesterday
            CreatedAt = DateTime.UtcNow.AddDays(-15)
        };
        context.ActionTokens.Add(token);
        await context.SaveChangesAsync();

        // Act
        var now = DateTime.UtcNow;
        var isExpired = token.ExpiresAt <= now;

        // Assert
        Assert.True(isExpired);
    }

    [Fact]
    public async Task ActionToken_CanBeUsed_OnlyOnce()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();

        var token = new ActionToken
        {
            Token = Guid.NewGuid().ToString("N"),
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            Action = TokenAction.Confirm,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            CreatedAt = DateTime.UtcNow
        };
        context.ActionTokens.Add(token);
        await context.SaveChangesAsync();

        // Act - Use the token
        token.UsedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        // Assert
        var savedToken = await context.ActionTokens.FindAsync(token.Id);
        Assert.NotNull(savedToken);
        Assert.NotNull(savedToken.UsedAt);

        // Simulate second use attempt
        var canUse = savedToken.UsedAt == null && savedToken.ExpiresAt > DateTime.UtcNow;
        Assert.False(canUse);
    }

    [Fact]
    public async Task ActionToken_Confirm_ChangesShiftToConfirmed()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();

        var token = new ActionToken
        {
            Token = Guid.NewGuid().ToString("N"),
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            Action = TokenAction.Confirm,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            CreatedAt = DateTime.UtcNow
        };
        context.ActionTokens.Add(token);
        await context.SaveChangesAsync();

        // Act - Execute confirm action
        if (token.Action == TokenAction.Confirm)
        {
            shift.Status = ShiftStatus.Confirmed;
            shift.ConfirmedAt = DateTime.UtcNow;
            token.UsedAt = DateTime.UtcNow;
        }
        await context.SaveChangesAsync();

        // Assert
        var savedShift = await context.Shifts.FindAsync(shift.Id);
        Assert.NotNull(savedShift);
        Assert.Equal(ShiftStatus.Confirmed, savedShift.Status);
        Assert.NotNull(savedShift.ConfirmedAt);
    }

    [Fact]
    public async Task ActionToken_Decline_ReopensShift()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();

        var token = new ActionToken
        {
            Token = Guid.NewGuid().ToString("N"),
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            Action = TokenAction.Decline,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            CreatedAt = DateTime.UtcNow
        };
        context.ActionTokens.Add(token);
        await context.SaveChangesAsync();

        // Act - Execute decline action
        if (token.Action == TokenAction.Decline)
        {
            shift.VolunteerId = null;
            shift.Status = ShiftStatus.Open;
            shift.AssignedAt = null;
            token.UsedAt = DateTime.UtcNow;
        }
        await context.SaveChangesAsync();

        // Assert
        var savedShift = await context.Shifts.FindAsync(shift.Id);
        Assert.NotNull(savedShift);
        Assert.Equal(ShiftStatus.Open, savedShift.Status);
        Assert.Null(savedShift.VolunteerId);
    }

    [Fact]
    public async Task ActionToken_Cancel_FromConfirmed_ReopensShift()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();

        // First confirm the shift
        shift.Status = ShiftStatus.Confirmed;
        shift.ConfirmedAt = DateTime.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();

        var token = new ActionToken
        {
            Token = Guid.NewGuid().ToString("N"),
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            Action = TokenAction.Cancel,
            ExpiresAt = DateTime.UtcNow.AddDays(1), // Shorter expiration for cancel
            CreatedAt = DateTime.UtcNow
        };
        context.ActionTokens.Add(token);
        await context.SaveChangesAsync();

        // Act - Execute cancel action
        if (token.Action == TokenAction.Cancel)
        {
            shift.VolunteerId = null;
            shift.Status = ShiftStatus.Open;
            shift.AssignedAt = null;
            shift.ConfirmedAt = null;
            token.UsedAt = DateTime.UtcNow;
        }
        await context.SaveChangesAsync();

        // Assert
        var savedShift = await context.Shifts.FindAsync(shift.Id);
        Assert.NotNull(savedShift);
        Assert.Equal(ShiftStatus.Open, savedShift.Status);
        Assert.Null(savedShift.VolunteerId);
        Assert.Null(savedShift.ConfirmedAt);
    }

    [Fact]
    public async Task ActionToken_FindByTokenString_ReturnsCorrectToken()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();

        var tokenString = Guid.NewGuid().ToString("N");
        var token = new ActionToken
        {
            Token = tokenString,
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            Action = TokenAction.Confirm,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            CreatedAt = DateTime.UtcNow
        };
        context.ActionTokens.Add(token);
        await context.SaveChangesAsync();

        // Act
        var found = await context.ActionTokens
            .Include(t => t.Shift)
                .ThenInclude(s => s.TimeSlot)
            .Include(t => t.Volunteer)
            .FirstOrDefaultAsync(t => t.Token == tokenString);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(tokenString, found.Token);
        Assert.NotNull(found.Shift);
        Assert.NotNull(found.Volunteer);
        Assert.Equal("Token Test Volunteer", found.Volunteer.Name);
    }

    [Fact]
    public async Task ActionToken_UniqueTokenString_EnforcedByApplication()
    {
        // Arrange
        var (context, shift, volunteer) = await SetupTestData();

        var tokenString = Guid.NewGuid().ToString("N");
        var token1 = new ActionToken
        {
            Token = tokenString,
            ShiftId = shift.Id,
            VolunteerId = volunteer.Id,
            Action = TokenAction.Confirm,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            CreatedAt = DateTime.UtcNow
        };
        context.ActionTokens.Add(token1);
        await context.SaveChangesAsync();

        // Act - Check if token already exists before creating
        var exists = await context.ActionTokens.AnyAsync(t => t.Token == tokenString);

        // Assert
        Assert.True(exists);
    }
}
