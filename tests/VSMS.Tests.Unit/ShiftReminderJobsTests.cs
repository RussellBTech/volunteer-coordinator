using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Core.Interfaces;
using VSMS.Infrastructure.Data;
using VSMS.Jobs;

namespace VSMS.Tests.Unit;

public class ShiftReminderJobsTests
{
    private VsmsDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<VsmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new VsmsDbContext(options);
    }

    private (TimeSlot slot, Volunteer volunteer) SetupTestData(VsmsDbContext context)
    {
        var timeSlot = new TimeSlot
        {
            Label = "Morning",
            StartTime = new TimeOnly(9, 0),
            DurationMinutes = 180,
            IsActive = true,
            SortOrder = 1
        };
        context.TimeSlots.Add(timeSlot);

        var volunteer = new Volunteer
        {
            Name = "Test Volunteer",
            Email = "test@example.com",
            IsActive = true
        };
        context.Volunteers.Add(volunteer);

        context.SaveChanges();
        return (timeSlot, volunteer);
    }

    #region SendSevenDayReminders Tests

    [Fact]
    public async Task SendSevenDayReminders_SendsToUnconfirmedShiftsPublished7DaysAgo()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (timeSlot, volunteer) = SetupTestData(context);
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        var shift = new Shift
        {
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Assigned,
            AssignedAt = DateTime.UtcNow.AddDays(-8),
            ReminderSentAt7Days = false
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.SendSevenDayReminders();

        // Assert
        Assert.Single(emailService.ReminderEmailsSent);
        Assert.Equal(volunteer.Email, emailService.ReminderEmailsSent[0].volunteer.Email);
    }

    [Fact]
    public async Task SendSevenDayReminders_MarksReminderAsSent()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (timeSlot, volunteer) = SetupTestData(context);
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        var shift = new Shift
        {
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Assigned,
            AssignedAt = DateTime.UtcNow.AddDays(-10),
            ReminderSentAt7Days = false
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.SendSevenDayReminders();

        // Assert
        var updatedShift = await context.Shifts.FindAsync(shift.Id);
        Assert.True(updatedShift!.ReminderSentAt7Days);
    }

    [Fact]
    public async Task SendSevenDayReminders_SkipsAlreadySentReminders()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (timeSlot, volunteer) = SetupTestData(context);
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        var shift = new Shift
        {
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Assigned,
            AssignedAt = DateTime.UtcNow.AddDays(-8),
            ReminderSentAt7Days = true // Already sent
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.SendSevenDayReminders();

        // Assert
        Assert.Empty(emailService.ReminderEmailsSent);
    }

    [Fact]
    public async Task SendSevenDayReminders_SkipsConfirmedShifts()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (timeSlot, volunteer) = SetupTestData(context);
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        var shift = new Shift
        {
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Confirmed, // Already confirmed
            AssignedAt = DateTime.UtcNow.AddDays(-8),
            ReminderSentAt7Days = false
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.SendSevenDayReminders();

        // Assert
        Assert.Empty(emailService.ReminderEmailsSent);
    }

    #endregion

    #region Send24HourReminders Tests

    [Fact]
    public async Task Send24HourReminders_SendsToConfirmedShiftsStartingSoon()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        var timeSlot = new TimeSlot
        {
            Label = "Morning",
            StartTime = TimeOnly.FromDateTime(DateTime.UtcNow.AddHours(24)),
            DurationMinutes = 180,
            IsActive = true,
            SortOrder = 1
        };
        context.TimeSlots.Add(timeSlot);

        var volunteer = new Volunteer
        {
            Name = "Test Volunteer",
            Email = "test@example.com",
            IsActive = true
        };
        context.Volunteers.Add(volunteer);
        await context.SaveChangesAsync();

        var shift = new Shift
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(24)),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Confirmed,
            ReminderSentAt24Hours = false
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.Send24HourReminders();

        // Assert
        Assert.Single(emailService.Hour24RemindersSent);
        Assert.Equal(volunteer.Email, emailService.Hour24RemindersSent[0].volunteer.Email);
    }

    [Fact]
    public async Task Send24HourReminders_MarksReminderAsSent()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        var timeSlot = new TimeSlot
        {
            Label = "Morning",
            StartTime = TimeOnly.FromDateTime(DateTime.UtcNow.AddHours(24)),
            DurationMinutes = 180,
            IsActive = true,
            SortOrder = 1
        };
        context.TimeSlots.Add(timeSlot);

        var volunteer = new Volunteer
        {
            Name = "Test Volunteer",
            Email = "test@example.com",
            IsActive = true
        };
        context.Volunteers.Add(volunteer);
        await context.SaveChangesAsync();

        var shift = new Shift
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(24)),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Confirmed,
            ReminderSentAt24Hours = false
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.Send24HourReminders();

        // Assert
        var updatedShift = await context.Shifts.FindAsync(shift.Id);
        Assert.True(updatedShift!.ReminderSentAt24Hours);
    }

    [Fact]
    public async Task Send24HourReminders_SkipsUnconfirmedShifts()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        var timeSlot = new TimeSlot
        {
            Label = "Morning",
            StartTime = TimeOnly.FromDateTime(DateTime.UtcNow.AddHours(24)),
            DurationMinutes = 180,
            IsActive = true,
            SortOrder = 1
        };
        context.TimeSlots.Add(timeSlot);

        var volunteer = new Volunteer
        {
            Name = "Test Volunteer",
            Email = "test@example.com",
            IsActive = true
        };
        context.Volunteers.Add(volunteer);
        await context.SaveChangesAsync();

        var shift = new Shift
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(24)),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Assigned, // Not confirmed
            ReminderSentAt24Hours = false
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.Send24HourReminders();

        // Assert
        Assert.Empty(emailService.Hour24RemindersSent);
    }

    #endregion

    #region AutoReopenUnconfirmedShifts Tests

    [Fact]
    public async Task AutoReopenUnconfirmedShifts_ReopensUnconfirmedShiftStartingSoon()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        // Create a shift that starts 12 hours from now (within the 24-hour window)
        var now = DateTime.UtcNow;
        var shiftDateTime = now.AddHours(12);
        var shiftDate = DateOnly.FromDateTime(shiftDateTime);
        var shiftTime = TimeOnly.FromDateTime(shiftDateTime);

        var timeSlot = new TimeSlot
        {
            Label = "Soon",
            StartTime = shiftTime,
            DurationMinutes = 180,
            IsActive = true,
            SortOrder = 1
        };
        context.TimeSlots.Add(timeSlot);

        var volunteer = new Volunteer
        {
            Name = "Unreliable Volunteer",
            Email = "unreliable@example.com",
            IsActive = true
        };
        context.Volunteers.Add(volunteer);
        await context.SaveChangesAsync();

        var shift = new Shift
        {
            Date = shiftDate,
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.InPerson,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Assigned,
            AssignedAt = DateTime.UtcNow.AddDays(-7)
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.AutoReopenUnconfirmedShifts();

        // Assert
        var updatedShift = await context.Shifts.FindAsync(shift.Id);
        Assert.Equal(ShiftStatus.Open, updatedShift!.Status);
        Assert.Null(updatedShift.VolunteerId);
        Assert.Null(updatedShift.AssignedAt);
    }

    [Fact]
    public async Task AutoReopenUnconfirmedShifts_NotifiesAdmin()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        var now = DateTime.UtcNow;
        var shiftDateTime = now.AddHours(12);
        var shiftDate = DateOnly.FromDateTime(shiftDateTime);
        var shiftTime = TimeOnly.FromDateTime(shiftDateTime);

        var timeSlot = new TimeSlot
        {
            Label = "Soon",
            StartTime = shiftTime,
            DurationMinutes = 180,
            IsActive = true,
            SortOrder = 1
        };
        context.TimeSlots.Add(timeSlot);

        var volunteer = new Volunteer
        {
            Name = "Test Volunteer",
            Email = "test@example.com",
            IsActive = true
        };
        context.Volunteers.Add(volunteer);
        await context.SaveChangesAsync();

        var shift = new Shift
        {
            Date = shiftDate,
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.InPerson,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Assigned
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.AutoReopenUnconfirmedShifts();

        // Assert
        Assert.Single(emailService.AdminNotificationsSent);
    }

    [Fact]
    public async Task AutoReopenUnconfirmedShifts_SkipsBackupEscalationUntilFeatureImplemented()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        var now = DateTime.UtcNow;
        var shiftDateTime = now.AddHours(12);
        var shiftDate = DateOnly.FromDateTime(shiftDateTime);
        var shiftTime = TimeOnly.FromDateTime(shiftDateTime);

        var timeSlot = new TimeSlot
        {
            Label = "Soon",
            StartTime = shiftTime,
            DurationMinutes = 180,
            IsActive = true,
            SortOrder = 1
        };
        context.TimeSlots.Add(timeSlot);

        var assignedVolunteer = new Volunteer
        {
            Name = "Assigned Volunteer",
            Email = "assigned@example.com",
            IsActive = true
        };
        context.Volunteers.Add(assignedVolunteer);
        await context.SaveChangesAsync();

        var shift = new Shift
        {
            Date = shiftDate,
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.InPerson,
            VolunteerId = assignedVolunteer.Id,
            Status = ShiftStatus.Assigned
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.AutoReopenUnconfirmedShifts();

        // Assert - backup escalation is disabled until backup slot feature is implemented
        Assert.Empty(emailService.BackupEscalationsSent);
    }

    [Fact]
    public async Task AutoReopenUnconfirmedShifts_CreatesAuditLogEntry()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        var now = DateTime.UtcNow;
        var shiftDateTime = now.AddHours(12);
        var shiftDate = DateOnly.FromDateTime(shiftDateTime);
        var shiftTime = TimeOnly.FromDateTime(shiftDateTime);

        var timeSlot = new TimeSlot
        {
            Label = "Soon",
            StartTime = shiftTime,
            DurationMinutes = 180,
            IsActive = true,
            SortOrder = 1
        };
        context.TimeSlots.Add(timeSlot);

        var volunteer = new Volunteer
        {
            Name = "Test Volunteer",
            Email = "test@example.com",
            IsActive = true
        };
        context.Volunteers.Add(volunteer);
        await context.SaveChangesAsync();

        var shift = new Shift
        {
            Date = shiftDate,
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.InPerson,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Assigned
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.AutoReopenUnconfirmedShifts();

        // Assert
        var auditLog = await context.AuditLogEntries.FirstOrDefaultAsync(a => a.ShiftId == shift.Id);
        Assert.NotNull(auditLog);
        Assert.Equal("Auto-Reopened", auditLog.Action);
        Assert.Contains("Test Volunteer", auditLog.Details);
    }

    [Fact]
    public async Task AutoReopenUnconfirmedShifts_SkipsConfirmedShifts()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var emailService = new FakeEmailService();
        var logger = new FakeLogger<ShiftReminderJobs>();

        var now = DateTime.UtcNow;
        var shiftDateTime = now.AddHours(12);
        var shiftDate = DateOnly.FromDateTime(shiftDateTime);
        var shiftTime = TimeOnly.FromDateTime(shiftDateTime);

        var timeSlot = new TimeSlot
        {
            Label = "Soon",
            StartTime = shiftTime,
            DurationMinutes = 180,
            IsActive = true,
            SortOrder = 1
        };
        context.TimeSlots.Add(timeSlot);

        var volunteer = new Volunteer
        {
            Name = "Reliable Volunteer",
            Email = "reliable@example.com",
            IsActive = true
        };
        context.Volunteers.Add(volunteer);
        await context.SaveChangesAsync();

        var shift = new Shift
        {
            Date = shiftDate,
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.InPerson,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Confirmed, // Already confirmed
            ConfirmedAt = DateTime.UtcNow.AddDays(-1)
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var tokenService = new FakeTokenService();
        var jobs = new ShiftReminderJobs(context, emailService, tokenService, logger);

        // Act
        await jobs.AutoReopenUnconfirmedShifts();

        // Assert
        var updatedShift = await context.Shifts.FindAsync(shift.Id);
        Assert.Equal(ShiftStatus.Confirmed, updatedShift!.Status);
        Assert.NotNull(updatedShift.VolunteerId);
        Assert.Empty(emailService.AdminNotificationsSent);
    }

    #endregion
}

#region Test Doubles

public class FakeEmailService : IEmailService
{
    public List<(Volunteer volunteer, List<Shift> shifts)> MonthlyEmailsSent { get; } = new();
    public List<(Volunteer volunteer, List<Shift> shifts)> ReminderEmailsSent { get; } = new();
    public List<(Volunteer volunteer, Shift shift)> Hour24RemindersSent { get; } = new();
    public List<(Volunteer volunteer, Shift shift)> RequestReceivedEmailsSent { get; } = new();
    public List<(Volunteer volunteer, Shift shift)> ApprovalEmailsSent { get; } = new();
    public List<Shift> AdminNotificationsSent { get; } = new();
    public List<(Shift shift, List<Volunteer> backups)> BackupEscalationsSent { get; } = new();
    public List<(Shift shift, List<Volunteer> volunteers)> AllEscalationsSent { get; } = new();

    public Task SendMonthlyAssignmentEmailAsync(Volunteer volunteer, List<Shift> shifts)
    {
        MonthlyEmailsSent.Add((volunteer, shifts));
        return Task.CompletedTask;
    }

    public Task SendReminderEmailAsync(Volunteer volunteer, List<Shift> unconfirmedShifts)
    {
        ReminderEmailsSent.Add((volunteer, unconfirmedShifts));
        return Task.CompletedTask;
    }

    public Task Send24HourReminderAsync(Volunteer volunteer, Shift shift)
    {
        Hour24RemindersSent.Add((volunteer, shift));
        return Task.CompletedTask;
    }

    public Task SendShiftRequestReceivedAsync(Volunteer volunteer, Shift shift)
    {
        RequestReceivedEmailsSent.Add((volunteer, shift));
        return Task.CompletedTask;
    }

    public Task SendShiftApprovedAsync(Volunteer volunteer, Shift shift)
    {
        ApprovalEmailsSent.Add((volunteer, shift));
        return Task.CompletedTask;
    }

    public Task SendShiftReopenedToAdminAsync(Shift shift)
    {
        AdminNotificationsSent.Add(shift);
        return Task.CompletedTask;
    }

    public Task SendEscalationToBackupsAsync(Shift shift, List<Volunteer> backups)
    {
        BackupEscalationsSent.Add((shift, backups));
        return Task.CompletedTask;
    }

    public Task SendEscalationToAllAsync(Shift shift, List<Volunteer> volunteers)
    {
        AllEscalationsSent.Add((shift, volunteers));
        return Task.CompletedTask;
    }
}

public class FakeLogger<T> : ILogger<T>
{
    public List<string> LogMessages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        LogMessages.Add($"[{logLevel}] {formatter(state, exception)}");
    }
}

public class FakeTokenService : ITokenService
{
    public int CleanupCallCount { get; private set; }
    public int TokensToDelete { get; set; } = 0;

    public Task<ActionToken> CreateTokenAsync(int shiftId, int volunteerId, TokenAction action, int? expirationDays = null)
    {
        return Task.FromResult(new ActionToken
        {
            Token = Guid.NewGuid().ToString("N"),
            ShiftId = shiftId,
            VolunteerId = volunteerId,
            Action = action,
            ExpiresAt = DateTime.UtcNow.AddDays(expirationDays ?? 14),
            CreatedAt = DateTime.UtcNow
        });
    }

    public string GenerateActionUrl(ActionToken token) => $"https://test.com/action/{token.Token}";

    public string GenerateActionUrl(string tokenValue) => $"https://test.com/action/{tokenValue}";

    public Task<int> CleanupExpiredTokensAsync()
    {
        CleanupCallCount++;
        return Task.FromResult(TokensToDelete);
    }
}

#endregion
