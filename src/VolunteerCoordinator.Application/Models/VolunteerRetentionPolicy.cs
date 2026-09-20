namespace VolunteerCoordinator.Application.Models;

public static class VolunteerRetentionPolicy
{
    public const int MinimumRetentionDays = 365;
    public const int MaximumRetentionDays = 365;
    public const int MinimumSweepIntervalHours = 24;
    public const int MaximumSweepIntervalHours = 24;
    public const int MinimumBatchSize = 1;
    public const int MaximumBatchSize = 100;
}
