using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator;

public sealed class AccessModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;
    private readonly CoordinatorOptions _options;
    private readonly IConfiguration _configuration;

    public AccessModel(
        VolunteerCoordinatorService service,
        IOptions<CoordinatorOptions> options,
        IConfiguration configuration)
    {
        _service = service;
        _options = options.Value;
        _configuration = configuration;
    }

    public CoordinatorAccessDiagnosticsDto Diagnostics { get; private set; } =
        new(false, [], null, []);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostVerifyAsync(CancellationToken cancellationToken)
    {
        await _service.VerifyCoordinatorAccessAsync(
            CoordinatorIdentity.GetEmail(User) ?? string.Empty,
            cancellationToken);
        TempData["Message"] = "Your independent coordinator access was verified.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var oidcConfigured =
            !string.IsNullOrWhiteSpace(_configuration["Oidc:Authority"]) &&
            !string.IsNullOrWhiteSpace(_configuration["Oidc:ClientId"]) &&
            !string.IsNullOrWhiteSpace(_configuration["Oidc:ClientSecret"]);
        Diagnostics = await _service.GetCoordinatorAccessDiagnosticsAsync(
            oidcConfigured,
            _options.AllowedEmails,
            CoordinatorIdentity.GetEmail(User),
            cancellationToken);
    }
}
