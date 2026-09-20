using VolunteerCoordinator.Application.Models;

namespace VolunteerCoordinator.Web.Presentation;

public sealed record ConsequencePreviewViewModel(
    CoordinatorActionPreviewDto Preview,
    string Heading,
    string Description,
    string ConfirmAction,
    string BackUrl,
    IReadOnlyList<PreviewHiddenField> HiddenFields,
    IReadOnlyList<string> Errors,
    string? EditAction = null,
    IReadOnlyList<PreviewHiddenField>? EditFields = null,
    string EditLabel = "Change selection or details");
