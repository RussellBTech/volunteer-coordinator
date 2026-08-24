using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;

namespace VolunteerCoordinator.Infrastructure.Notifications;

public sealed class UnavailableNotificationService : INotificationService
{
    private readonly VolunteerCoordinatorDbContext _dbContext;
    private readonly IClock _clock;

    public UnavailableNotificationService(VolunteerCoordinatorDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<NotificationResult> RecordAndSendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken)
    {
        var attempt = NotificationAttempt.Create(
            message.TransitionId,
            message.Kind,
            message.Destination,
            _clock.UtcNow);
        attempt.Fail(_clock.UtcNow, "No transactional notification provider is configured.");
        _dbContext.NotificationAttempts.Add(attempt);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new NotificationResult(false, "The workflow succeeded; notification delivery is not configured.");
    }
}
