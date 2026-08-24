using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Volunteers;

namespace VolunteerCoordinator.Infrastructure.Persistence;

public sealed class VolunteerCoordinatorDbContext : DbContext
{
    public VolunteerCoordinatorDbContext(DbContextOptions<VolunteerCoordinatorDbContext> options)
        : base(options)
    {
    }

    public DbSet<Shift> Shifts => Set<Shift>();

    public DbSet<ShiftSlot> ShiftSlots => Set<ShiftSlot>();

    public DbSet<Volunteer> Volunteers => Set<Volunteer>();

    public DbSet<ShiftRequest> ShiftRequests => Set<ShiftRequest>();

    public DbSet<Assignment> Assignments => Set<Assignment>();

    public DbSet<ActionToken> ActionTokens => Set<ActionToken>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<NotificationAttempt> NotificationAttempts => Set<NotificationAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {
        var shift = modelBuilder.Entity<Shift>();
        shift.ToTable("Shifts", table => table.HasCheckConstraint(
            "CK_Shifts_Interval",
            "\"EndsAtUtc\" > \"StartsAtUtc\""));
        shift.HasKey(x => x.Id);
        shift.Property(x => x.Title).HasMaxLength(120).IsRequired();
        shift.Property(x => x.Location).HasMaxLength(200);
        shift.Property(x => x.Notes).HasMaxLength(1000);
        shift.Property(x => x.StartsAtUtc).HasColumnType("timestamp with time zone");
        shift.Property(x => x.EndsAtUtc).HasColumnType("timestamp with time zone");
        shift.Property(x => x.PublishedAtUtc).HasColumnType("timestamp with time zone");
        shift.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        shift.Property(x => x.Version).IsRowVersion();
        shift.HasIndex(x => new { x.IsActive, x.PublishedAtUtc, x.StartsAtUtc, x.EndsAtUtc })
            .HasDatabaseName("IX_Shifts_PublicOpening");
        shift.HasMany(x => x.Slots)
            .WithOne()
            .HasForeignKey(x => x.ShiftId)
            .OnDelete(DeleteBehavior.Cascade);
        shift.Navigation(x => x.Slots).HasField("_slots").UsePropertyAccessMode(PropertyAccessMode.Field);

        var slot = modelBuilder.Entity<ShiftSlot>();
        slot.ToTable("ShiftSlots", table => table.HasCheckConstraint(
            "CK_ShiftSlots_Position",
            "(\"Kind\" = 0 AND \"Position\" = 1) OR (\"Kind\" = 1 AND \"Position\" BETWEEN 1 AND 2)"));
        slot.HasKey(x => x.Id);
        slot.Property(x => x.Kind).HasConversion<int>();
        slot.HasIndex(x => new { x.ShiftId, x.Kind, x.Position }).IsUnique();

        var volunteer = modelBuilder.Entity<Volunteer>();
        volunteer.ToTable("Volunteers");
        volunteer.HasKey(x => x.Id);
        volunteer.Property(x => x.Name).HasMaxLength(120).IsRequired();
        volunteer.Property(x => x.Email).HasMaxLength(320).IsRequired();
        volunteer.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
        volunteer.Property(x => x.Phone).HasMaxLength(40);
        volunteer.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        volunteer.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        volunteer.HasIndex(x => x.NormalizedEmail).IsUnique();

        var request = modelBuilder.Entity<ShiftRequest>();
        request.ToTable("ShiftRequests", table => table.HasCheckConstraint(
            "CK_ShiftRequests_StatusTokenHash",
            "octet_length(\"StatusTokenHash\") = 32"));
        request.HasKey(x => x.Id);
        request.Property(x => x.Status).HasConversion<int>().IsConcurrencyToken();
        request.Property(x => x.RequestedAtUtc).HasColumnType("timestamp with time zone");
        request.Property(x => x.ResolvedAtUtc).HasColumnType("timestamp with time zone");
        request.Property(x => x.ResolvedByCoordinatorEmail).HasMaxLength(320);
        request.Property(x => x.StatusTokenHash).HasColumnType("bytea").IsRequired();
        request.Property(x => x.StatusTokenExpiresAtUtc).HasColumnType("timestamp with time zone");
        request.HasOne<ShiftSlot>().WithMany().HasForeignKey(x => x.ShiftSlotId).OnDelete(DeleteBehavior.Restrict);
        request.HasOne<Volunteer>().WithMany().HasForeignKey(x => x.VolunteerId).OnDelete(DeleteBehavior.Restrict);
        request.HasIndex(x => x.StatusTokenHash).IsUnique();
        request.HasIndex(x => new { x.ShiftSlotId, x.VolunteerId })
            .IsUnique()
            .HasFilter("\"Status\" = 0")
            .HasDatabaseName("UX_ShiftRequests_Pending");

        var assignment = modelBuilder.Entity<Assignment>();
        assignment.ToTable("Assignments");
        assignment.HasKey(x => x.Id);
        assignment.Property(x => x.Status).HasConversion<int>().IsConcurrencyToken();
        assignment.Property(x => x.AssignedAtUtc).HasColumnType("timestamp with time zone");
        assignment.Property(x => x.ConfirmedAtUtc).HasColumnType("timestamp with time zone");
        assignment.Property(x => x.EndedAtUtc).HasColumnType("timestamp with time zone");
        assignment.Property(x => x.AssignedByCoordinatorEmail).HasMaxLength(320).IsRequired();
        assignment.Ignore(x => x.IsActive);
        assignment.HasOne<ShiftSlot>().WithMany().HasForeignKey(x => x.ShiftSlotId).OnDelete(DeleteBehavior.Restrict);
        assignment.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        assignment.HasOne<Volunteer>().WithMany().HasForeignKey(x => x.VolunteerId).OnDelete(DeleteBehavior.Restrict);
        assignment.HasOne<ShiftRequest>().WithMany().HasForeignKey(x => x.SourceRequestId).OnDelete(DeleteBehavior.SetNull);
        assignment.HasIndex(x => x.ShiftSlotId)
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1)")
            .HasDatabaseName("UX_Assignments_ActiveSlot");
        assignment.HasIndex(x => new { x.ShiftId, x.VolunteerId })
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1)")
            .HasDatabaseName("UX_Assignments_ActiveVolunteerShift");

        var actionToken = modelBuilder.Entity<ActionToken>();
        actionToken.ToTable("ActionTokens", table => table.HasCheckConstraint(
            "CK_ActionTokens_TokenHash",
            "octet_length(\"TokenHash\") = 32"));
        actionToken.HasKey(x => x.Id);
        actionToken.Property(x => x.Action).HasConversion<int>();
        actionToken.Property(x => x.TokenHash).HasColumnType("bytea").IsRequired();
        actionToken.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        actionToken.Property(x => x.ExpiresAtUtc).HasColumnType("timestamp with time zone");
        actionToken.Property(x => x.UsedAtUtc).HasColumnType("timestamp with time zone").IsConcurrencyToken();
        actionToken.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
        actionToken.HasIndex(x => x.TokenHash).IsUnique();
        actionToken.HasIndex(x => new { x.AssignmentId, x.Action })
            .IsUnique()
            .HasFilter("\"UsedAtUtc\" IS NULL")
            .HasDatabaseName("UX_ActionTokens_UnusedAssignmentAction");

        var audit = modelBuilder.Entity<AuditEntry>();
        audit.ToTable("AuditEntries");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.OccurredAtUtc).HasColumnType("timestamp with time zone");
        audit.Property(x => x.Actor).HasMaxLength(320).IsRequired();
        audit.Property(x => x.Action).HasMaxLength(100).IsRequired();
        audit.Property(x => x.EntityKind).HasMaxLength(100).IsRequired();
        audit.Property(x => x.DetailJson).HasColumnType("jsonb").IsRequired();
        audit.HasIndex(x => x.OccurredAtUtc);

        var notification = modelBuilder.Entity<NotificationAttempt>();
        notification.ToTable("NotificationAttempts");
        notification.HasKey(x => x.Id);
        notification.Property(x => x.Kind).HasMaxLength(100).IsRequired();
        notification.Property(x => x.Destination).HasMaxLength(320).IsRequired();
        notification.Property(x => x.State).HasConversion<int>();
        notification.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        notification.Property(x => x.CompletedAtUtc).HasColumnType("timestamp with time zone");
        notification.Property(x => x.ErrorSummary).HasMaxLength(500);
        notification.HasIndex(x => new { x.TransitionId, x.State });
    }
}
