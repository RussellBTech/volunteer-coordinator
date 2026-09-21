using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.Web.Pages.Coordinator;

public sealed class MessagesModel : PageModel
{
    private readonly VolunteerCoordinatorService _service;

    public MessagesModel(VolunteerCoordinatorService service)
    {
        _service = service;
    }

    public CoordinatorMessagePageDto MessagePage { get; private set; } = new(1, 50, 0, []);

    public IReadOnlyList<CoordinatorMessageDto> Messages => MessagePage.Messages;
    public IReadOnlyList<NotificationIntentDto> NotificationIntents { get; private set; } = [];

    public string? AppliedAttention { get; private set; }

    public async Task OnGetAsync(
        string? attention,
        int page,
        CancellationToken cancellationToken)
    {
        AppliedAttention = attention switch
        {
            null or "" => null,
            "message" => "message",
            _ => null
        };
        MessagePage = await _service.GetActionableMessagesPageAsync(page, cancellationToken);
        NotificationIntents = await _service.ListNotificationIntentsAsync(cancellationToken);
    }
    public async Task<IActionResult> OnPostResendAsync(
        Guid intentId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _service.RequestNotificationResendAsync(
                intentId,
                CoordinatorIdentity.GetEmail(User)!,
                cancellationToken);
            TempData["Success"] = "Another copy was queued.";
        }
        catch (VolunteerCoordinator.Domain.DomainException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage();
    }
}
