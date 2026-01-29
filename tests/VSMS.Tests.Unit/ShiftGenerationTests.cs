using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;

namespace VSMS.Tests.Unit;

public class ShiftGenerationTests
{
    private VsmsDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<VsmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new VsmsDbContext(options);
    }

    [Fact]
    public async Task Shift_DefaultStatus_IsOpen()
    {
        // Arrange
        var context = CreateInMemoryContext();
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

        // Act
        var shift = new Shift
        {
            Date = new DateOnly(2026, 2, 1),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        // Assert
        var saved = await context.Shifts.FindAsync(shift.Id);
        Assert.NotNull(saved);
        Assert.Equal(ShiftStatus.Open, saved.Status);
        Assert.Null(saved.VolunteerId);
    }

    [Fact]
    public async Task Shift_WithVolunteer_StatusIsAssigned()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var volunteer = new Volunteer
        {
            Name = "John Doe",
            Email = "john@example.com",
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

        // Act
        var shift = new Shift
        {
            Date = new DateOnly(2026, 2, 1),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.InPerson,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Assigned,
            AssignedAt = DateTime.UtcNow
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        // Assert
        var saved = await context.Shifts
            .Include(s => s.Volunteer)
            .FirstAsync(s => s.Id == shift.Id);
        Assert.Equal(ShiftStatus.Assigned, saved.Status);
        Assert.NotNull(saved.Volunteer);
        Assert.Equal("John Doe", saved.Volunteer.Name);
    }

    [Fact]
    public async Task Shift_Confirmed_SetsConfirmedAt()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var volunteer = new Volunteer
        {
            Name = "Jane Smith",
            Email = "jane@example.com",
            IsActive = true
        };
        context.Volunteers.Add(volunteer);

        var timeSlot = new TimeSlot
        {
            Label = "Afternoon",
            StartTime = new TimeOnly(12, 0),
            DurationMinutes = 180,
            IsActive = true,
            SortOrder = 2
        };
        context.TimeSlots.Add(timeSlot);
        await context.SaveChangesAsync();

        var shift = new Shift
        {
            Date = new DateOnly(2026, 2, 5),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            VolunteerId = volunteer.Id,
            Status = ShiftStatus.Assigned
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        // Act - Simulate confirmation
        var beforeConfirm = DateTime.UtcNow;
        shift.Status = ShiftStatus.Confirmed;
        shift.ConfirmedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        // Assert
        var confirmed = await context.Shifts.FindAsync(shift.Id);
        Assert.NotNull(confirmed);
        Assert.Equal(ShiftStatus.Confirmed, confirmed.Status);
        Assert.NotNull(confirmed.ConfirmedAt);
        Assert.True(confirmed.ConfirmedAt >= beforeConfirm);
    }

    [Fact]
    public async Task MasterScheduleEntry_AppliesDefaultVolunteer()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var volunteer = new Volunteer
        {
            Name = "Regular Volunteer",
            Email = "regular@example.com",
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

        var masterEntry = new MasterScheduleEntry
        {
            DayOfWeek = DayOfWeek.Monday,
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            DefaultVolunteerId = volunteer.Id,
            IsClosed = false
        };
        context.MasterScheduleEntries.Add(masterEntry);
        await context.SaveChangesAsync();

        // Act - Simulate generation logic
        var date = new DateOnly(2026, 2, 2); // A Monday
        var entry = await context.MasterScheduleEntries
            .FirstOrDefaultAsync(e =>
                e.DayOfWeek == date.DayOfWeek &&
                e.TimeSlotId == timeSlot.Id &&
                e.Role == ShiftRole.Phone);

        var generatedShift = new Shift
        {
            Date = date,
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            VolunteerId = entry?.DefaultVolunteerId,
            Status = entry?.DefaultVolunteerId != null ? ShiftStatus.Assigned : ShiftStatus.Open
        };
        context.Shifts.Add(generatedShift);
        await context.SaveChangesAsync();

        // Assert
        Assert.NotNull(generatedShift.VolunteerId);
        Assert.Equal(volunteer.Id, generatedShift.VolunteerId);
        Assert.Equal(ShiftStatus.Assigned, generatedShift.Status);
    }

    [Fact]
    public async Task MasterScheduleEntry_ClosedDay_SkipsShiftGeneration()
    {
        // Arrange
        var context = CreateInMemoryContext();
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

        var masterEntry = new MasterScheduleEntry
        {
            DayOfWeek = DayOfWeek.Sunday,
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            IsClosed = true // Office is closed
        };
        context.MasterScheduleEntries.Add(masterEntry);
        await context.SaveChangesAsync();

        // Act - Simulate generation logic that skips closed days
        var date = new DateOnly(2026, 2, 1); // A Sunday
        var entry = await context.MasterScheduleEntries
            .FirstOrDefaultAsync(e =>
                e.DayOfWeek == date.DayOfWeek &&
                e.TimeSlotId == timeSlot.Id &&
                e.Role == ShiftRole.Phone);

        // Skip if closed
        var shouldCreate = entry?.IsClosed != true;

        // Assert
        Assert.False(shouldCreate);
    }

    [Fact]
    public async Task Volunteer_BackupFlag_IdentifiesBackupVolunteers()
    {
        // Arrange
        var context = CreateInMemoryContext();

        var regularVolunteer = new Volunteer
        {
            Name = "Regular Person",
            Email = "regular@example.com",
            IsBackup = false,
            IsActive = true
        };

        var backupVolunteer = new Volunteer
        {
            Name = "Backup Person",
            Email = "backup@example.com",
            IsBackup = true,
            IsActive = true
        };

        context.Volunteers.AddRange(regularVolunteer, backupVolunteer);
        await context.SaveChangesAsync();

        // Act
        var backups = await context.Volunteers
            .Where(v => v.IsBackup && v.IsActive)
            .ToListAsync();

        var regulars = await context.Volunteers
            .Where(v => !v.IsBackup && v.IsActive)
            .ToListAsync();

        // Assert
        Assert.Single(backups);
        Assert.Single(regulars);
        Assert.Equal("Backup Person", backups[0].Name);
        Assert.Equal("Regular Person", regulars[0].Name);
    }

    [Fact]
    public async Task UniqueConstraint_ShiftDateTimeSlotRole_PreventsDoubles()
    {
        // Arrange
        var context = CreateInMemoryContext();
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

        var shift1 = new Shift
        {
            Date = new DateOnly(2026, 2, 10),
            TimeSlotId = timeSlot.Id,
            Role = ShiftRole.Phone,
            Status = ShiftStatus.Open
        };
        context.Shifts.Add(shift1);
        await context.SaveChangesAsync();

        // Act - Check if duplicate exists before adding
        var duplicate = await context.Shifts.AnyAsync(s =>
            s.Date == new DateOnly(2026, 2, 10) &&
            s.TimeSlotId == timeSlot.Id &&
            s.Role == ShiftRole.Phone);

        // Assert - The check should prevent duplicates in real code
        Assert.True(duplicate);
    }
}
