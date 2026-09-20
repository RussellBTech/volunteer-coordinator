using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Volunteers;
using VolunteerCoordinator.Infrastructure.Persistence;

namespace VolunteerCoordinator.Infrastructure.Notifications;

public sealed class UnavailableNotificationService : INotificationService
{
    private static readonly TimeSpan PersistenceTimeout = TimeSpan.FromSeconds(5);
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
        using var persistenceTimeout = new CancellationTokenSource(PersistenceTimeout);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            persistenceTimeout.Token);
        try
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "Volunteers" WHERE "Id" = {message.VolunteerId} FOR UPDATE""",
                persistenceTimeout.Token);
            var volunteer = await ReloadVolunteerAsync(
                message.VolunteerId,
                persistenceTimeout.Token);
            if (volunteer is null || volunteer.AnonymizedAtUtc.HasValue)
            {
                await transaction.CommitAsync(persistenceTimeout.Token);
                return new NotificationResult(
                    false,
                    "The workflow succeeded; notification was skipped because volunteer contact data was removed.");
            }

            var now = _clock.UtcNow;
            var attempt = NotificationAttempt.Create(
                message.TransitionId,
                message.Kind,
                volunteer.Email,
                now);
            attempt.Fail(now, "No transactional notification provider is configured.");
            _dbContext.NotificationAttempts.Add(attempt);
            await _dbContext.SaveChangesAsync(persistenceTimeout.Token);
            await transaction.CommitAsync(persistenceTimeout.Token);
            return new NotificationResult(
                false,
                "The workflow succeeded; notification delivery is not configured.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<Volunteer?> ReloadVolunteerAsync(
        Guid volunteerId,
        CancellationToken cancellationToken)
    {
        var trackedEntry = _dbContext.ChangeTracker
            .Entries<Volunteer>()
            .SingleOrDefault(x => x.Entity.Id == volunteerId);
        if (trackedEntry is not null)
        {
            await trackedEntry.ReloadAsync(cancellationToken);
            return trackedEntry.State == EntityState.Detached ? null : trackedEntry.Entity;
        }

        return await _dbContext.Volunteers
            .SingleOrDefaultAsync(x => x.Id == volunteerId, cancellationToken);
    }
}
