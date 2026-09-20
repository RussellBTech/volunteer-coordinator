using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Presentation;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator.Assignments;

public sealed class AssignModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;
    private readonly CoordinatorReviewStateProtector _reviewStateProtector;

    public AssignModel(
        VolunteerCoordinatorService service,
        CoordinatorReviewStateProtector reviewStateProtector)
    {
        _service = service;
        _reviewStateProtector = reviewStateProtector;
    }

    public CoverageDto? Coverage { get; private set; }

    public Guid? CurrentAssignmentId => Coverage?.AssignmentId;
    public bool SettingsMissing { get; private set; }

    public Guid SlotId { get; private set; }

    public IReadOnlyList<VolunteerDto> ExistingVolunteers { get; private set; } = [];

    public string Mode { get; private set; } = "choose";

    public CoordinatorActionPreviewDto? Preview { get; private set; }

    public ConsequencePreviewViewModel? Review { get; private set; }
    public PreviewRecoveryViewModel? Recovery { get; private set; }

    [BindProperty]
    public Guid? KnownVolunteerId { get; set; }

    [BindProperty, StringLength(120)]
    [Display(Name = "Volunteer name")]
    public string? VolunteerName { get; set; }

    [BindProperty, EmailAddress, StringLength(320)]
    [Display(Name = "Volunteer email")]
    public string? VolunteerEmail { get; set; }

    [BindProperty, Display(Name = "Phone (optional)"), Phone, StringLength(40)]
    public string? VolunteerPhone { get; set; }

    [BindProperty]
    public string? ReviewToken { get; set; }

    public async Task<IActionResult> OnGetAsync(
        Guid slotId,
        string? mode,
        CancellationToken cancellationToken)
    {
        SlotId = slotId;
        Mode = NormalizeMode(mode);
        if (!await LoadAsync(slotId, cancellationToken))
        {
            return SettingsMissing ? RedirectToPage("/Coordinator/Settings") : NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostReviewAsync(
        Guid slotId,
        CancellationToken cancellationToken)
    {
        SlotId = slotId;
        Mode = NormalizeMode(Request.Form["Mode"].ToString());
        if (!await LoadAsync(slotId, cancellationToken, recoveryOnMissing: true))
        {
            return SettingsMissing
                ? RedirectToPage("/Coordinator/Settings")
                : Recovery is not null ? Page() : NotFound();
        }

        if (!ValidateSelection())
        {
            return Page();
        }

        try
        {
            Preview = await _service.GetAssignmentPreviewAsync(
                slotId,
                Mode == "known" ? KnownVolunteerId : null,
                Mode == "new" ? VolunteerName : null,
                Mode == "new" ? VolunteerEmail : null,
                Mode == "new" ? VolunteerPhone : null,
                cancellationToken);
            BuildReview();
            return Page();
        }
        catch (DomainException exception)
        {
            if (IsPreviewTargetUnavailable(exception.Message))
            {
                Recovery = new PreviewRecoveryViewModel(
                    VolunteerCoordinatorService.StalePreviewMessage,
                    "/Coordinator/Coverage");
            }
            else
            {
                ModelState.AddModelError(string.Empty, exception.Message);
            }
            return Page();
        }
    }
    public async Task<IActionResult> OnPostEditAsync(
        Guid slotId,
        CancellationToken cancellationToken)
    {
        SlotId = slotId;
        if (!TryGetReviewState(slotId, out var state))
        {
            return await RenderInvalidReviewStateAsync(slotId, cancellationToken);
        }

        ApplyReviewState(state);
        if (!await LoadAsync(slotId, cancellationToken, recoveryOnMissing: true))
        {
            return SettingsMissing
                ? RedirectToPage("/Coordinator/Settings")
                : Recovery is not null ? Page() : NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(
        Guid slotId,
        CancellationToken cancellationToken)
    {
        SlotId = slotId;
        if (!TryGetReviewState(slotId, out var state))
        {
            return await RenderInvalidReviewStateAsync(slotId, cancellationToken);
        }

        ApplyReviewState(state);
        if (!await LoadAsync(slotId, cancellationToken, recoveryOnMissing: true))
        {
            return SettingsMissing
                ? RedirectToPage("/Coordinator/Settings")
                : Recovery is not null ? Page() : NotFound();
        }

        try
        {
            var result = await _service.AssignVolunteerAsync(
                state.SlotId,
                state.ExpectedAssignmentId,
                state.ExpectedVolunteerId,
                state.ExpectedAssignmentState,
                state.ExpectedShiftVersion,
                state.Mode == "known" ? state.KnownVolunteerId : null,
                state.Mode == "new" ? state.VolunteerName : null,
                state.Mode == "new" ? state.VolunteerEmail : null,
                state.Mode == "new" ? state.VolunteerPhone : null,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken,
                state.ExpectedSettingsVersion,
                state.ExpectedAffectedSet,
                state.ExpectedSelectedVolunteerId,
                state.ExpectedSelectedVolunteerNormalizedEmail);
            TempData["Message"] = state.ActionKey == "replace"
                ? "Volunteer replaced. Review the updated coverage state."
                : "Volunteer assigned. Review the updated coverage state.";
            TempData["Warning"] = result.NotificationWarning;
            return RedirectToPage("/Coordinator/Coverage/Index");
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            try
            {
                Preview = await _service.GetAssignmentPreviewAsync(
                    slotId,
                    state.Mode == "known" ? state.KnownVolunteerId : null,
                    state.Mode == "new" ? state.VolunteerName : null,
                    state.Mode == "new" ? state.VolunteerEmail : null,
                    state.Mode == "new" ? state.VolunteerPhone : null,
                    cancellationToken);
                BuildReview();
            }
            catch (DomainException previewException)
            {
                if (IsPreviewTargetUnavailable(previewException.Message))
                {
                    Recovery = new PreviewRecoveryViewModel(
                        VolunteerCoordinatorService.StalePreviewMessage,
                        "/Coordinator/Coverage");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, previewException.Message);
                }
            }

            return Page();
        }
    }

    private async Task<IActionResult> RenderInvalidReviewStateAsync(
        Guid slotId,
        CancellationToken cancellationToken)
    {
        Mode = "choose";
        if (!await LoadAsync(slotId, cancellationToken, recoveryOnMissing: true))
        {
            return SettingsMissing
                ? RedirectToPage("/Coordinator/Settings")
                : Recovery is not null ? Page() : NotFound();
        }

        Recovery = new PreviewRecoveryViewModel(
            "This review is no longer available. Choose the volunteer again.",
            "/Coordinator/Coverage");
        return Page();
    }

    private bool TryGetReviewState(
        Guid slotId,
        out CoordinatorAssignmentReviewState state)
    {
        state = null!;
        if (!_reviewStateProtector.TryUnprotect(ReviewToken, out var protectedState) ||
            protectedState is null ||
            protectedState.SlotId != slotId)
        {
            return false;
        }

        state = protectedState;
        return true;
    }

    private void ApplyReviewState(CoordinatorAssignmentReviewState state)
    {
        Mode = state.Mode;
        KnownVolunteerId = state.KnownVolunteerId;
        VolunteerName = state.VolunteerName;
        VolunteerEmail = state.VolunteerEmail;
        VolunteerPhone = state.VolunteerPhone;
    }

    private async Task<bool> LoadAsync(
        Guid slotId,
        CancellationToken cancellationToken,
        bool recoveryOnMissing = false)
    {
        SettingsMissing = await _service.GetGroupSettingsAsync(cancellationToken) is null;
        if (SettingsMissing)
        {
            return false;
        }

        Coverage = (await _service.GetCoverageAsync(cancellationToken))
            .SingleOrDefault(x => x.SlotId == slotId);
        ExistingVolunteers = await _service.ListEligibleVolunteersAsync(cancellationToken);
        if (Coverage is null && recoveryOnMissing)
        {
            Recovery = new PreviewRecoveryViewModel(
                VolunteerCoordinatorService.StalePreviewMessage,
                "/Coordinator/Coverage");
        }

        return Coverage is not null;
    }
    private static bool IsPreviewTargetUnavailable(string message) =>
        message.Contains("no longer", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("not found", StringComparison.OrdinalIgnoreCase);

    private bool ValidateSelection()
    {
        if (Mode == "known")
        {
            if (!KnownVolunteerId.HasValue || KnownVolunteerId == Guid.Empty)
            {
                ModelState.AddModelError(nameof(KnownVolunteerId), "Choose a volunteer by name and email.");
            }
        }
        else if (Mode == "new")
        {
            if (string.IsNullOrWhiteSpace(VolunteerName))
            {
                ModelState.AddModelError(nameof(VolunteerName), "Enter the volunteer's name.");
            }

            if (string.IsNullOrWhiteSpace(VolunteerEmail))
            {
                ModelState.AddModelError(nameof(VolunteerEmail), "Enter the volunteer's email.");
            }
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Choose whether to select a known volunteer or add someone new.");
        }

        return ModelState.IsValid;
    }

    private void BuildReview()
    {
        if (Preview is null)
        {
            return;
        }

        var state = new CoordinatorAssignmentReviewState(
            SlotId,
            Preview.ActionKey,
            Mode,
            Mode == "known" ? KnownVolunteerId : null,
            Mode == "new" ? VolunteerName : null,
            Mode == "new" ? VolunteerEmail : null,
            Mode == "new" ? VolunteerPhone : null,
            Preview.ExpectedAssignmentId,
            Preview.ExpectedVolunteerId,
            Preview.ExpectedAssignmentState,
            Preview.ExpectedShiftVersion,
            Preview.ExpectedSettingsVersion,
            Preview.ExpectedAffectedSet,
            Preview.ExpectedSelectedVolunteerId,
            Preview.ExpectedSelectedVolunteerNormalizedEmail);
        ReviewToken = _reviewStateProtector.Protect(state);
        var protectedFields = new[]
        {
            new PreviewHiddenField("ReviewToken", ReviewToken!)
        };

        Review = new ConsequencePreviewViewModel(
            Preview,
            Preview.IsReplacement ? "Review volunteer replacement" : "Review volunteer assignment",
            Preview.IsReplacement
                ? "Check the current volunteer, replacement details, local commitment, and every affected request or assignment before confirming."
                : "Check the volunteer, local commitment, and every affected request or assignment before confirming.",
            $"/Coordinator/Assignments/Assign/{SlotId}?handler=Confirm",
            $"/Coordinator/Assignments/Assign/{SlotId}?mode={Mode}#assignment-options",
            protectedFields,
            ModelState[string.Empty]?.Errors.Select(x => x.ErrorMessage).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? [],
            $"/Coordinator/Assignments/Assign/{SlotId}?handler=Edit",
            protectedFields);
    }

    private static string NormalizeMode(string? mode) =>
        mode switch
        {
            "known" => "known",
            "new" => "new",
            _ => "choose"
        };
}
