using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;

namespace VolunteerCoordinator.Web.Pages.Recurring;

public sealed class HubModel : PageModel
{
    private readonly RecurringCommitmentService _service;

    public HubModel(RecurringCommitmentService service)
    {
        _service = service;
    }

    public RecurringCommitmentHubDto? Hub { get; private set; }
    public string? Error { get; private set; }

    [BindProperty]
    public DateOnly? EffectiveLocalDate { get; set; }

    public async Task OnGetAsync(string token, CancellationToken cancellationToken)
    {
        SetPrivateHeaders();
        await LoadAsync(token, cancellationToken);
    }

    public async Task<IActionResult> OnPostConfirmAsync(string token, CancellationToken cancellationToken)
    {
        SetPrivateHeaders();
        try
        {
            await _service.ApplyHubActionAsync(token, "Confirm", null, cancellationToken);
            return RedirectToPage(new { token });
        }
        catch (DomainException)
        {
            Error = "This recurring commitment link is invalid, expired, or the action is no longer available.";
            await LoadAsync(token, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostWithdrawAsync(string token, CancellationToken cancellationToken)
    {
        SetPrivateHeaders();
        try
        {
            await _service.ApplyHubActionAsync(token, "Withdraw", EffectiveLocalDate, cancellationToken);
            return RedirectToPage(new { token });
        }
        catch (DomainException exception)
        {
            Error = exception.Message;
            await LoadAsync(token, cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            Hub = await _service.InspectHubAsync(token, cancellationToken);
            if (EffectiveLocalDate is null)
            {
                EffectiveLocalDate = Hub.WithdrawalDates.FirstOrDefault();
            }
        }
        catch (DomainException exception)
        {
            Error ??= exception.Message;
        }
    }

    private void SetPrivateHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
