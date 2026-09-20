namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorHomeDto(
    bool IsSetupMode,
    IReadOnlyList<SetupStepDto> SetupSteps,
    IReadOnlyList<CoordinatorAttentionDto> Attention,
    string? RecommendedActionLabel,
    string? RecommendedActionUrl,
    string? GroupTimeZoneId)
{
    public bool IsCaughtUp => !IsSetupMode && Attention.Count == 0;

    public int PendingRequestCount => Find("pending")?.Count ?? 0;

    public int UncoveredCommitmentCount => Find("uncovered")?.Count ?? 0;

    public int UnconfirmedAssignmentCount => Find("unconfirmed")?.Count ?? 0;

    public int FailedMessageCount => Find("message")?.Count ?? 0;

    private CoordinatorAttentionDto? Find(string key) =>
        Attention.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal));
}
