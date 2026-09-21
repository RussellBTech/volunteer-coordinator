namespace VolunteerCoordinator.Application.Models;

public sealed class CoordinatorAttentionOptions
{
    public const string SectionName = "CoordinatorAttention";

    public int UrgentHours { get; init; } = 24;

    public int SoonHours { get; init; } = 72;

    public int PageSize { get; init; } = 50;

    public int HomeExampleLimit { get; init; } = 3;

    public bool IsValid() =>
        UrgentHours > 0 &&
        SoonHours > UrgentHours &&
        PageSize is > 0 and <= 50 &&
        HomeExampleLimit is > 0 and <= 3;
}
