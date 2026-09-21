using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Domain.Access;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Settings;
using VolunteerCoordinator.Domain.Volunteers;

namespace VolunteerCoordinator.Infrastructure.Persistence;

public sealed class VolunteerCoordinatorDbContext : DbContext
{
    public VolunteerCoordinatorDbContext(DbContextOptions<VolunteerCoordinatorDbContext> options)
        : base(options)
    {
    }

    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<RecurringShiftSeries> RecurringShiftSeries => Set<RecurringShiftSeries>();

    public DbSet<RecurringShiftSeriesRevision> RecurringShiftSeriesRevisions => Set<RecurringShiftSeriesRevision>();

    public DbSet<RecurringShiftOccurrence> RecurringShiftOccurrences => Set<RecurringShiftOccurrence>();


    public DbSet<GroupSettings> GroupSettings => Set<GroupSettings>();

    public DbSet<ShiftSlot> ShiftSlots => Set<ShiftSlot>();

    public DbSet<Volunteer> Volunteers => Set<Volunteer>();

    public DbSet<ShiftRequest> ShiftRequests => Set<ShiftRequest>();

    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<VolunteerAccessCapability> VolunteerAccessCapabilities => Set<VolunteerAccessCapability>();

    public DbSet<RecoveryToken> RecoveryTokens => Set<RecoveryToken>();

    public DbSet<NotificationIntent> NotificationIntents => Set<NotificationIntent>();

    public DbSet<NotificationDeliveryAttempt> NotificationDeliveryAttempts => Set<NotificationDeliveryAttempt>();

    public DbSet<ResendWebhookReceipt> ResendWebhookReceipts => Set<ResendWebhookReceipt>();

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
        shift.Property(x => x.Title).HasMaxLength(120).IsRequired();
        shift.Property(x => x.Notes).HasMaxLength(1000);
        shift.Property(x => x.VolunteerInstructions).HasMaxLength(1000);
        shift.Property(x => x.Location).HasMaxLength(200);
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
        shift.HasOne<RecurringShiftOccurrence>()
            .WithOne()
            .HasForeignKey<Shift>(x => x.RecurringOccurrenceId)
            .OnDelete(DeleteBehavior.Restrict);
        shift.HasIndex(x => x.RecurringOccurrenceId)
            .IsUnique()
            .HasFilter("\"RecurringOccurrenceId\" IS NOT NULL")
            .HasDatabaseName("UX_Shifts_RecurringOccurrence");

        var recurringSeries = modelBuilder.Entity<RecurringShiftSeries>();
        recurringSeries.ToTable("RecurringShiftSeries");
        recurringSeries.HasKey(x => x.Id);
        recurringSeries.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        recurringSeries.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        recurringSeries.Property(x => x.LastGeneratedThroughLocalDate).HasColumnType("date");
        recurringSeries.Property(x => x.Version).IsRowVersion();
        recurringSeries.HasIndex(x => new { x.IsActive, x.UpdatedAtUtc })
            .HasDatabaseName("IX_RecurringShiftSeries_Active");

        var recurringRevision = modelBuilder.Entity<RecurringShiftSeriesRevision>();
        recurringRevision.ToTable("RecurringShiftSeriesRevisions", table =>
        {
            table.HasCheckConstraint(
                "CK_RecurringShiftSeriesRevisions_Interval",
                "\"Interval\" BETWEEN 1 AND 4");
            table.HasCheckConstraint(
                "CK_RecurringShiftSeriesRevisions_Duration",
                "\"DurationMinutes\" BETWEEN 1 AND 10080");
            table.HasCheckConstraint(
                "CK_RecurringShiftSeriesRevisions_Horizon",
                "\"HorizonWeeks\" BETWEEN 4 AND 26");
            table.HasCheckConstraint(
                "CK_RecurringShiftSeriesRevisions_BackupSlots",
                "\"BackupSlotCount\" BETWEEN 0 AND 2");
        });
        recurringRevision.HasKey(x => x.Id);
        recurringRevision.Property(x => x.Title).HasMaxLength(120).IsRequired();
        recurringRevision.Property(x => x.Location).HasMaxLength(200);
        recurringRevision.Property(x => x.VolunteerInstructions).HasMaxLength(1000);
        recurringRevision.Property(x => x.InternalCoordinatorNotes).HasMaxLength(1000);
        recurringRevision.Property(x => x.RecurrenceKind).HasConversion<int>();
        recurringRevision.Property(x => x.WeeklyDays).HasConversion<int>();
        recurringRevision.Property(x => x.AmbiguousTimeChoice).HasConversion<int>();
        recurringRevision.Property(x => x.AnchorLocalDate).HasColumnType("date");
        recurringRevision.Property(x => x.EffectiveLocalDate).HasColumnType("date");
        recurringRevision.Property(x => x.LocalStartTime).HasColumnType("time without time zone");
        recurringRevision.Property(x => x.TimeZoneId).HasMaxLength(200).IsRequired();
        recurringRevision.Property(x => x.CreatedByCoordinator).HasMaxLength(320).IsRequired();
        recurringRevision.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        recurringRevision.HasOne<RecurringShiftSeries>()
            .WithMany()
            .HasForeignKey(x => x.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);
        recurringRevision.HasIndex(x => new { x.SeriesId, x.RevisionNumber }).IsUnique();
        recurringRevision.HasIndex(x => new { x.SeriesId, x.EffectiveLocalDate }).IsUnique();

        var recurringOccurrence = modelBuilder.Entity<RecurringShiftOccurrence>();
        recurringOccurrence.ToTable("RecurringShiftOccurrences", table =>
            table.HasCheckConstraint(
                "CK_RecurringShiftOccurrences_State",
                "(\"Status\" = 0 AND \"ShiftId\" IS NOT NULL) OR (\"Status\" IN (1, 2) AND \"ShiftId\" IS NULL)"));
        recurringOccurrence.HasKey(x => x.Id);
        recurringOccurrence.Property(x => x.RevisionId).IsRequired();
        recurringOccurrence.Property(x => x.LocalDate).HasColumnType("date");
        recurringOccurrence.Property(x => x.Status).HasConversion<int>();
        recurringOccurrence.Property(x => x.ResolvedLocalStart).HasColumnType("timestamp without time zone");
        recurringOccurrence.Property(x => x.ResolutionActor).HasMaxLength(320);
        recurringOccurrence.Property(x => x.ResolutionAtUtc).HasColumnType("timestamp with time zone");
        recurringOccurrence.Property(x => x.ResolutionReason).HasMaxLength(1000);
        recurringOccurrence.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        recurringOccurrence.Property(x => x.Version).IsRowVersion();
        recurringOccurrence.HasOne<RecurringShiftSeries>()
            .WithMany()
            .HasForeignKey(x => x.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);
        recurringOccurrence.HasOne<RecurringShiftSeriesRevision>()
            .WithMany()
            .HasForeignKey(x => x.RevisionId)
            .OnDelete(DeleteBehavior.Restrict);
        recurringOccurrence.HasIndex(x => new { x.SeriesId, x.LocalDate }).IsUnique();
        recurringOccurrence.HasIndex(x => x.Status).HasDatabaseName("IX_RecurringShiftOccurrences_Status");
        recurringOccurrence.HasIndex(x => x.ShiftId)
            .IsUnique()
            .HasFilter("\"ShiftId\" IS NOT NULL")
            .HasDatabaseName("UX_RecurringShiftOccurrences_Shift");


        var groupSettings = modelBuilder.Entity<GroupSettings>();
        groupSettings.ToTable("GroupSettings", table => table.HasCheckConstraint(
            "CK_GroupSettings_Singleton",
            $"\"Id\" = '{VolunteerCoordinator.Domain.Settings.GroupSettings.SingletonId}'"));
        groupSettings.HasKey(x => x.Id);
        groupSettings.Property(x => x.TimeZoneId).HasMaxLength(200).IsRequired();
        groupSettings.Property(x => x.Version).IsRowVersion();

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
        volunteer.Property(x => x.AnonymizedAtUtc).HasColumnType("timestamp with time zone");
        volunteer.HasIndex(x => x.NormalizedEmail).IsUnique();
        volunteer.HasIndex(x => x.AnonymizedAtUtc)
            .HasDatabaseName("IX_Volunteers_AnonymizedAtUtc");

        var request = modelBuilder.Entity<ShiftRequest>();
        request.ToTable("ShiftRequests");
        request.HasKey(x => x.Id);
        request.Property(x => x.Status).HasConversion<int>().IsConcurrencyToken();
        request.Property(x => x.RequestedAtUtc).HasColumnType("timestamp with time zone");
        request.Property(x => x.ResolvedAtUtc).HasColumnType("timestamp with time zone");
        request.Property(x => x.ResolvedByCoordinatorEmail).HasMaxLength(320);
        request.HasOne<ShiftSlot>().WithMany().HasForeignKey(x => x.ShiftSlotId).OnDelete(DeleteBehavior.Restrict);
        request.HasOne<Volunteer>().WithMany().HasForeignKey(x => x.VolunteerId).OnDelete(DeleteBehavior.Restrict);
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
        var capability = modelBuilder.Entity<VolunteerAccessCapability>();
        capability.ToTable("VolunteerAccessCapabilities", table => table.HasCheckConstraint(
            "CK_VolunteerAccessCapabilities_TokenHash",
            "octet_length(\"TokenHash\") = 32"));
        capability.HasKey(x => x.Id);
        capability.Property(x => x.TokenHash).HasColumnType("bytea").IsRequired();
        capability.Property(x => x.IssuedReason).HasConversion<int>();
        capability.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        capability.Property(x => x.InvalidatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();
        capability.HasOne<ShiftSlot>().WithMany().HasForeignKey(x => x.ShiftSlotId).OnDelete(DeleteBehavior.Restrict);
        capability.HasOne<Volunteer>().WithMany().HasForeignKey(x => x.VolunteerId).OnDelete(DeleteBehavior.Restrict);
        capability.HasIndex(x => x.TokenHash).IsUnique();
        capability.HasIndex(x => new { x.ShiftSlotId, x.VolunteerId })
            .IsUnique()
            .HasFilter("\"InvalidatedAtUtc\" IS NULL")
            .HasDatabaseName("UX_VolunteerAccessCapabilities_ActiveCommitment");

        var recovery = modelBuilder.Entity<RecoveryToken>();
        recovery.ToTable("RecoveryTokens", table => table.HasCheckConstraint(
            "CK_RecoveryTokens_TokenHash",
            "octet_length(\"TokenHash\") = 32"));
        recovery.HasKey(x => x.Id);
        recovery.Property(x => x.TokenHash).HasColumnType("bytea").IsRequired();
        recovery.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        recovery.Property(x => x.ExpiresAtUtc).HasColumnType("timestamp with time zone");
        recovery.Property(x => x.UsedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();
        recovery.Property(x => x.InvalidatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();
        recovery.HasOne<ShiftSlot>().WithMany().HasForeignKey(x => x.ShiftSlotId).OnDelete(DeleteBehavior.Restrict);
        recovery.HasOne<Volunteer>().WithMany().HasForeignKey(x => x.VolunteerId).OnDelete(DeleteBehavior.Restrict);
        recovery.HasIndex(x => x.TokenHash).IsUnique();
        recovery.HasIndex(x => new { x.VolunteerId, x.ShiftSlotId, x.ExpiresAtUtc });

        var intent = modelBuilder.Entity<NotificationIntent>();
        intent.ToTable("NotificationIntents");
        intent.HasKey(x => x.Id);
        intent.Property(x => x.EventKey).HasMaxLength(240).IsRequired();
        intent.Property(x => x.Kind).HasMaxLength(100).IsRequired();
        intent.Property(x => x.State).HasConversion<int>();
        intent.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        intent.Property(x => x.NextAttemptAtUtc).HasColumnType("timestamp with time zone");
        intent.Property(x => x.LeaseUntilUtc).HasColumnType("timestamp with time zone");
        intent.Property(x => x.AcceptedAtUtc).HasColumnType("timestamp with time zone");
        intent.Property(x => x.DeliveredAtUtc).HasColumnType("timestamp with time zone");
        intent.Property(x => x.LastProviderEventAtUtc).HasColumnType("timestamp with time zone");
        intent.Property(x => x.CompletedAtUtc).HasColumnType("timestamp with time zone");
        intent.Property(x => x.Version).IsConcurrencyToken();
        intent.Property(x => x.ClaimOwnerToken);
        intent.Property(x => x.ProviderMessageId).HasMaxLength(200);
        intent.Property(x => x.FailureCategory).HasMaxLength(100);
        intent.HasOne<Volunteer>().WithMany().HasForeignKey(x => x.VolunteerId).OnDelete(DeleteBehavior.Restrict);
        intent.HasOne<ShiftSlot>().WithMany().HasForeignKey(x => x.ShiftSlotId).OnDelete(DeleteBehavior.Restrict);
        intent.HasOne<RecoveryToken>().WithMany().HasForeignKey(x => x.RecoveryTokenId).OnDelete(DeleteBehavior.SetNull);
        intent.HasIndex(x => x.EventKey)
            .IsUnique()
            .HasFilter("\"State\" IN (0, 1, 2)");
        intent.HasIndex(x => new { x.State, x.NextAttemptAtUtc, x.CreatedAtUtc });
        intent.HasIndex(x => x.ProviderMessageId).IsUnique().HasFilter("\"ProviderMessageId\" IS NOT NULL");

        var deliveryAttempt = modelBuilder.Entity<NotificationDeliveryAttempt>();
        deliveryAttempt.ToTable("NotificationDeliveryAttempts");
        deliveryAttempt.HasKey(x => x.Id);
        deliveryAttempt.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        deliveryAttempt.Property(x => x.OutcomeCategory).HasMaxLength(100);
        deliveryAttempt.Property(x => x.ProviderMessageId).HasMaxLength(200);
        deliveryAttempt.Property(x => x.StartedAtUtc).HasColumnType("timestamp with time zone");
        deliveryAttempt.Property(x => x.CompletedAtUtc).HasColumnType("timestamp with time zone");
        deliveryAttempt.HasOne<NotificationIntent>().WithMany().HasForeignKey(x => x.NotificationIntentId).OnDelete(DeleteBehavior.Cascade);
        deliveryAttempt.HasIndex(x => new { x.NotificationIntentId, x.Ordinal }).IsUnique();
        deliveryAttempt.HasIndex(x => x.IdempotencyKey).IsUnique();

        var webhook = modelBuilder.Entity<ResendWebhookReceipt>();
        webhook.ToTable("ResendWebhookReceipts");
        webhook.HasKey(x => x.Id);
        webhook.Property(x => x.SvixId).HasMaxLength(200).IsRequired();
        webhook.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        webhook.Property(x => x.ProviderMessageId).HasMaxLength(200).IsRequired();
        webhook.Property(x => x.ProviderOccurredAtUtc).HasColumnType("timestamp with time zone");
        webhook.Property(x => x.ProcessedAtUtc).HasColumnType("timestamp with time zone");
        webhook.HasIndex(x => x.SvixId).IsUnique();
        webhook.HasIndex(x => x.ProviderMessageId);

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
        notification.Property(x => x.State).HasConversion<int>();
        notification.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        notification.Property(x => x.CompletedAtUtc).HasColumnType("timestamp with time zone");
        notification.Property(x => x.ErrorSummary).HasMaxLength(500);
        notification.HasIndex(x => new { x.TransitionId, x.State });
    }
}
