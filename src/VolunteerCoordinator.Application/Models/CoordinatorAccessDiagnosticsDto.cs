namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorAccessVerificationDto(
    string NormalizedEmail,
    DateTimeOffset? VerifiedAtUtc);

public sealed record CoordinatorAccessDiagnosticsDto(
    bool OidcConfigured,
    IReadOnlyList<string> AllowlistedEmails,
    string? CurrentEmail,
    IReadOnlyList<CoordinatorAccessVerificationDto> Verifications)
{
    public bool HandoffReady =>
        OidcConfigured &&
        AllowlistedEmails.Count >= 2 &&
        Verifications.Count(x => x.VerifiedAtUtc.HasValue) >= 2;
}
