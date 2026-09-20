namespace VolunteerCoordinator.Web.Privacy;

public sealed class VolunteerRetentionInitializationState
{
    private int _completed;

    public bool IsCompleted => Volatile.Read(ref _completed) == 1;

    public void MarkCompleted() => Interlocked.Exchange(ref _completed, 1);
}
