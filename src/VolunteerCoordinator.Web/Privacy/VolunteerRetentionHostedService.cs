using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application;

namespace VolunteerCoordinator.Web.Privacy;

public sealed class VolunteerRetentionHostedService : BackgroundService
{
    private static readonly TimeSpan InitializationRetryDelay = TimeSpan.FromSeconds(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<VolunteerRetentionOptions> _options;
    private readonly VolunteerRetentionInitializationState _initializationState;
    private readonly ILogger<VolunteerRetentionHostedService> _logger;
    private Guid? _afterVolunteerId;

    public VolunteerRetentionHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<VolunteerRetentionOptions> options,
        VolunteerRetentionInitializationState initializationState,
        ILogger<VolunteerRetentionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _initializationState = initializationState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        if (!options.IsValid())
        {
            throw new InvalidOperationException("VolunteerRetention settings exceed the approved safe bounds.");
        }

        await CompleteInitialSweepAsync(options, stoppingToken);
        if (!_initializationState.IsCompleted || stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(options.SweepIntervalHours));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPeriodicSweepSafelyAsync(options, stoppingToken);
        }
    }

    private async Task CompleteInitialSweepAsync(
        VolunteerRetentionOptions options,
        CancellationToken cancellationToken)
    {
        Guid? afterVolunteerId = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var results = await RunSweepBatchAsync(
                    options,
                    afterVolunteerId,
                    cancellationToken);
                LogCandidateFailures(results);
                if (results.Count == 0)
                {
                    _initializationState.MarkCompleted();
                    _logger.LogInformation("Initial volunteer retention sweep completed before product routes were enabled.");
                    return;
                }

                afterVolunteerId = results[^1].VolunteerId;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Initial volunteer retention sweep could not complete; product routes remain unavailable until it succeeds.");
                try
                {
                    await Task.Delay(InitializationRetryDelay, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task RunPeriodicSweepSafelyAsync(
        VolunteerRetentionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await RunSweepBatchAsync(options, _afterVolunteerId, cancellationToken);
            LogCandidateFailures(results);
            if (results.Count == 0)
            {
                _afterVolunteerId = null;
            }
            else
            {
                _afterVolunteerId = results[^1].VolunteerId;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Volunteer retention sweep could not complete.");
        }
    }

    private async Task<IReadOnlyList<RetentionSweepResult>> RunSweepBatchAsync(
        VolunteerRetentionOptions options,
        Guid? afterVolunteerId,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<VolunteerCoordinatorService>();
        return await service.RunRetentionSweepAsync(
            options.RetentionDays,
            options.BatchSize,
            cancellationToken,
            afterVolunteerId);
    }

    private void LogCandidateFailures(IReadOnlyList<RetentionSweepResult> results)
    {
        foreach (var result in results.Where(x => x.Failed))
        {
            _logger.LogWarning(
                "Volunteer retention candidate {VolunteerId} failed with reason {Reason}; later candidates remain eligible for processing.",
                result.VolunteerId,
                result.FailureReason ?? "Domain validation failed.");
        }
    }
}
