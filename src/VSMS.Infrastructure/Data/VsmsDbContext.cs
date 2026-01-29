using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;

namespace VSMS.Infrastructure.Data;

public class VsmsDbContext : DbContext
{
    public VsmsDbContext(DbContextOptions<VsmsDbContext> options) : base(options)
    {
    }

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Volunteer> Volunteers => Set<Volunteer>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
    public DbSet<MasterScheduleEntry> MasterScheduleEntries => Set<MasterScheduleEntry>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ActionToken> ActionTokens => Set<ActionToken>();
    public DbSet<ShiftRequest> ShiftRequests => Set<ShiftRequest>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // AdminUser
        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.HasIndex(e => e.GoogleId).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // Volunteer
        modelBuilder.Entity<Volunteer>(entity =>
        {
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // TimeSlot
        modelBuilder.Entity<TimeSlot>(entity =>
        {
            entity.HasIndex(e => e.SortOrder);
        });

        // MasterScheduleEntry
        modelBuilder.Entity<MasterScheduleEntry>(entity =>
        {
            entity.HasIndex(e => new { e.DayOfWeek, e.TimeSlotId, e.Role }).IsUnique();

            entity.HasOne(e => e.TimeSlot)
                .WithMany(t => t.MasterScheduleEntries)
                .HasForeignKey(e => e.TimeSlotId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DefaultVolunteer)
                .WithMany(v => v.DefaultScheduleEntries)
                .HasForeignKey(e => e.DefaultVolunteerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Shift
        modelBuilder.Entity<Shift>(entity =>
        {
            entity.HasIndex(e => new { e.Date, e.TimeSlotId, e.Role }).IsUnique();
            entity.HasIndex(e => e.Date);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.TimeSlot)
                .WithMany(t => t.Shifts)
                .HasForeignKey(e => e.TimeSlotId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Volunteer)
                .WithMany(v => v.Shifts)
                .HasForeignKey(e => e.VolunteerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ActionToken
        modelBuilder.Entity<ActionToken>(entity =>
        {
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);

            entity.HasOne(e => e.Shift)
                .WithMany(s => s.ActionTokens)
                .HasForeignKey(e => e.ShiftId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Volunteer)
                .WithMany(v => v.ActionTokens)
                .HasForeignKey(e => e.VolunteerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ShiftRequest
        modelBuilder.Entity<ShiftRequest>(entity =>
        {
            entity.HasIndex(e => new { e.ShiftId, e.VolunteerId, e.Status });

            entity.HasOne(e => e.Shift)
                .WithMany(s => s.ShiftRequests)
                .HasForeignKey(e => e.ShiftId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Volunteer)
                .WithMany(v => v.ShiftRequests)
                .HasForeignKey(e => e.VolunteerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ResolvedByAdmin)
                .WithMany()
                .HasForeignKey(e => e.ResolvedByAdminId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AuditLogEntry
        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.ShiftId);
            entity.HasIndex(e => e.VolunteerId);

            entity.HasOne(e => e.Shift)
                .WithMany()
                .HasForeignKey(e => e.ShiftId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Volunteer)
                .WithMany()
                .HasForeignKey(e => e.VolunteerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.AdminUser)
                .WithMany()
                .HasForeignKey(e => e.AdminUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        // Seed default time slots
        modelBuilder.Entity<TimeSlot>().HasData(
            new TimeSlot { Id = 1, Label = "Morning", StartTime = new TimeOnly(9, 0), DurationMinutes = 180, SortOrder = 1 },
            new TimeSlot { Id = 2, Label = "Afternoon", StartTime = new TimeOnly(12, 0), DurationMinutes = 180, SortOrder = 2 },
            new TimeSlot { Id = 3, Label = "Evening", StartTime = new TimeOnly(15, 0), DurationMinutes = 180, SortOrder = 3 },
            // Saturday slots
            new TimeSlot { Id = 4, Label = "Saturday Morning", StartTime = new TimeOnly(10, 0), DurationMinutes = 180, SortOrder = 4 },
            new TimeSlot { Id = 5, Label = "Saturday Afternoon", StartTime = new TimeOnly(13, 0), DurationMinutes = 180, SortOrder = 5 }
        );
    }
}
