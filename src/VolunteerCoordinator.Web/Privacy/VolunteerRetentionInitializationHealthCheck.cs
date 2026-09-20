using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VolunteerCoordinator.Web.Privacy;

public sealed class VolunteerRetentionInitializationHealthCheck : IHealthCheck
{
    private readonly VolunteerRetentionInitializationState _state;

    public VolunteerRetentionInitializationHealthCheck(VolunteerRetentionInitializationState state)
    {
        _state = state;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            _state.IsCompleted
                ? HealthCheckResult.Healthy("Initial volunteer retention has completed.")
                : HealthCheckResult.Unhealthy("Initial volunteer retention has not completed."));
}
