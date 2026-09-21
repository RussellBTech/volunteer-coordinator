using VolunteerCoordinator.Domain.Notifications;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Notifications;

public sealed class NotificationIntentTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TerminalComplaintWinsOverLateDeliveredWebhook()
    {
        var intent = NotificationIntent.Create(
            "event-1",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AssignmentAccess",
            Created);
        intent.Claim(Created, Created.AddMinutes(2));
        intent.Accept(Created.AddSeconds(1), "provider-1");

        intent.ApplyWebhook("email.complained", Created.AddMinutes(3), Created.AddMinutes(3));
        intent.ApplyWebhook("email.delivered", Created.AddMinutes(2), Created.AddMinutes(4));

        Assert.Equal(NotificationIntentState.Complained, intent.State);
    }

    [Fact]
    public void TransientRetryUsesSafeStateWithoutChangingWorkflowIdentity()
    {
        var transitionId = Guid.NewGuid();
        var intent = NotificationIntent.Create(
            "event-2",
            transitionId,
            Guid.NewGuid(),
            null,
            "RequestReceipt",
            Created);

        intent.Claim(Created, Created.AddMinutes(2));
        intent.ScheduleRetry(Created, Created.AddMinutes(1), "ProviderTransientFailure");

        Assert.Equal(NotificationIntentState.RetryScheduled, intent.State);
        Assert.Equal(transitionId, intent.TransitionId);
        Assert.Equal(1, intent.AttemptCount);
    }

    [Fact]
    public void FifthTransientFailureBecomesFinalWithoutChangingTransitionIdentity()
    {
        var transitionId = Guid.NewGuid();
        var intent = NotificationIntent.Create(
            "event-final",
            transitionId,
            Guid.NewGuid(),
            null,
            "RequestReceipt",
            Created);

        for (var ordinal = 0; ordinal < 5; ordinal++)
        {
            intent.Claim(Created.AddMinutes(ordinal), Created.AddMinutes(ordinal + 2));
            intent.ScheduleRetry(
                Created.AddMinutes(ordinal),
                Created.AddMinutes(ordinal + 1),
                "Timeout");
        }

        Assert.Equal(NotificationIntentState.Failed, intent.State);
        Assert.Equal(transitionId, intent.TransitionId);
        Assert.Equal(5, intent.AttemptCount);
        Assert.Equal("Timeout", intent.FailureCategory);
        Assert.NotNull(intent.CompletedAtUtc);
    }

    [Fact]
    public void CancelledClaimRejectsLateProviderCompletionAndWebhook()
    {
        var intent = NotificationIntent.Create(
            "event-cancelled",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AssignmentAccess",
            Created);
        intent.Claim(Created, Created.AddMinutes(2));
        intent.Cancel(Created.AddSeconds(1), "CommitmentNoLongerEligible");

        intent.Accept(Created.AddSeconds(2), "provider-late");
        intent.ApplyWebhook("email.delivered", Created.AddSeconds(2), Created.AddSeconds(3));

        Assert.Equal(NotificationIntentState.Cancelled, intent.State);
        Assert.Null(intent.ProviderMessageId);
        Assert.Null(intent.ClaimOwnerToken);
    }

    [Fact]
    public void ProviderEventsDoNotRegressByOccurrenceTime()
    {
        var intent = NotificationIntent.Create(
            "event-order",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AssignmentAccess",
            Created);
        intent.Accept(Created, "provider-order");
        intent.ApplyWebhook("email.delivered", Created.AddMinutes(5), Created.AddMinutes(5));
        intent.ApplyWebhook("email.bounced", Created.AddMinutes(4), Created.AddMinutes(6));

        Assert.Equal(NotificationIntentState.Delivered, intent.State);
    }
}
