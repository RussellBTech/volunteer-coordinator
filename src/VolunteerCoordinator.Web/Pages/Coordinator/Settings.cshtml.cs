using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator;

public sealed class SettingsModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public SettingsModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public GroupSettingsDto? Settings { get; private set; }

    [BindProperty, Required, StringLength(200)]
    public string TimeZoneId { get; set; } = string.Empty;

    [BindProperty]
    public uint? ExpectedVersion { get; set; }

    [BindProperty]
    public bool ConfirmDisplayChange { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken, preserveInput: true);
            return Page();
        }

        try
        {
            await _service.ConfigureGroupTimeZoneAsync(
                TimeZoneId,
                ExpectedVersion,
                ConfirmDisplayChange,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Message"] = "Group time zone saved. Schedule times will use this local zone.";
            return RedirectToPage();
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(cancellationToken, preserveInput: true);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool preserveInput = false)
    {
        Settings = await _service.GetGroupSettingsAsync(cancellationToken);
        if (!preserveInput && Settings is not null)
        {
            TimeZoneId = Settings.TimeZoneId;
            ExpectedVersion = Settings.Version;
        }
    }
}
