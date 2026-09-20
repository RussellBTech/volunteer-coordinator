namespace VolunteerCoordinator.Web.Presentation;

public sealed record PreviewRecoveryViewModel(
    string Message,
    string BackUrl,
    string? EditUrl = null);
