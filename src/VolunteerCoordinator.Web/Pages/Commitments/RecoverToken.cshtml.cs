using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain;

namespace VolunteerCoordinator.Web.Pages.Commitments;

public sealed class RecoverTokenModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public RecoverTokenModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }
    public string? Error { get; private set; }

    public IActionResult OnGet(string token)
    {
        SetPrivateHeaders();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string token, CancellationToken cancellationToken)
    {
        SetPrivateHeaders();
        try
        {
            var hubToken = await _service.RedeemRecoveryAsync(token, cancellationToken);
            return RedirectToPage("/Requests/Status", new { token = hubToken });
        }
        catch (DomainException)
        {
            Error = "This recovery link is invalid or has expired.";
            return Page();
        }
    }

    private void SetPrivateHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
