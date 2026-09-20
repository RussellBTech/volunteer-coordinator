using VolunteerCoordinator.Application.Models;

namespace VolunteerCoordinator.Web.Privacy;

public sealed class VolunteerRetentionOptions
{
    public const string SectionName = "VolunteerRetention";

    public int RetentionDays { get; set; } = VolunteerRetentionPolicy.MinimumRetentionDays;

    public int SweepIntervalHours { get; set; } = 24;

    public int BatchSize { get; set; } = 100;

    public bool IsValid() =>
        RetentionDays == VolunteerRetentionPolicy.MinimumRetentionDays &&
        SweepIntervalHours == VolunteerRetentionPolicy.MinimumSweepIntervalHours &&
        BatchSize is >= VolunteerRetentionPolicy.MinimumBatchSize and <= VolunteerRetentionPolicy.MaximumBatchSize;
}
