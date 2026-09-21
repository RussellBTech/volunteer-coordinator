using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain;

namespace VolunteerCoordinator.Web.Pages.Commitments;

public sealed class RecoverModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public RecoverModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    [BindProperty]
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    public string CommitmentDate { get; set; } = string.Empty;

    public string? Receipt { get; private set; }

    public void OnGet()
    {
        SetPrivateHeaders();
        Receipt = TempData[nameof(Receipt)] as string;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        SetPrivateHeaders();
        if (!ModelState.IsValid ||
            !DateOnly.TryParseExact(
                CommitmentDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            ModelState.AddModelError(nameof(CommitmentDate), "Enter the commitment date.");
            return Page();
        }

        try
        {
            var result = await _service.RequestRecoveryAsync(Email, date, cancellationToken);
            TempData[nameof(Receipt)] = result.ReceiptMessage;
            return RedirectToPage();
        }
        catch (DomainException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
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
