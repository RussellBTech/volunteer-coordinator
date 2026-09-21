using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Domain.Commitments;
using VolunteerCoordinator.Domain.Access;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Infrastructure.Persistence;

namespace VolunteerCoordinator.Infrastructure.Notifications;

public sealed class NotificationDeliveryHostedService : BackgroundService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan[] RetryOffsets =
    [
        TimeSpan.Zero,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(120)
    ];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly IOptions<NotificationDeliveryOptions> _options;
    private readonly ILogger<NotificationDeliveryHostedService> _logger;

    private sealed class ClaimNoLongerCurrentException : Exception
    {
    }

    public NotificationDeliveryHostedService(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<NotificationDeliveryOptions> options,
        ILogger<NotificationDeliveryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.Value.PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Notification delivery cycle failed.");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VolunteerCoordinatorDbContext>();
        var renderer = scope.ServiceProvider.GetRequiredService<IEmailTemplateRenderer>();
        var provider = scope.ServiceProvider.GetRequiredService<ITransactionalEmailProvider>();
        var emailOptions = scope.ServiceProvider.GetRequiredService<IOptions<EmailOptions>>().Value;
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var transient = scope.ServiceProvider.GetRequiredService<ITransientLinkMaterialStore>();
        var claims = await ClaimAsync(db, cancellationToken);
        foreach (var claim in claims)
        {
            await ProcessAsync(
                db,
                claim,
                renderer,
                provider,
                emailOptions,
                tokens,
                transient,
                cancellationToken);
        }
    }

    private async Task<IReadOnlyList<NotificationClaim>> ClaimAsync(
        VolunteerCoordinatorDbContext db,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var candidates = await db.NotificationIntents
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM "NotificationIntents"
                WHERE (
                    ("State" IN (0, 1) AND "NextAttemptAtUtc" <= {now})
                    OR ("State" = 2 AND "LeaseUntilUtc" < {now})
                )
                ORDER BY "NextAttemptAtUtc", "CreatedAtUtc", "Id"
                LIMIT {Math.Clamp(_options.Value.BatchSize, 1, 25)}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
        var claims = new List<NotificationClaim>(candidates.Count);
        foreach (var intent in candidates)
        {
            if (intent.State == NotificationIntentState.InFlight)
            {
                var abandonedClaimOwner = intent.ClaimOwnerToken;
                var abandonedClaimVersion = intent.Version;
                var abandoned = await db.NotificationDeliveryAttempts
                    .Where(x => x.NotificationIntentId == intent.Id && x.CompletedAtUtc == null)
                    .OrderByDescending(x => x.Ordinal)
                    .FirstOrDefaultAsync(cancellationToken);
                abandoned?.Complete(now, "LeaseAbandoned");
                if (abandonedClaimOwner is Guid owner &&
                    intent.IsClaimOwned(owner) &&
                    intent.Version >= abandonedClaimVersion &&
                    intent.RecoveryTokenId is Guid recoveryTokenId)
                {
                    var recovery = await db.RecoveryTokens.SingleOrDefaultAsync(
                        x => x.Id == recoveryTokenId,
                        cancellationToken);
                    recovery?.Invalidate(now);
                    intent.ClearRecoveryToken();
                }
            }

            if (intent.AttemptCount >= RetryOffsets.Length)
            {
                intent.Fail(now, "RetryLimitExceeded");
                continue;
            }

            var ownerToken = intent.Claim(now, now.Add(LeaseDuration));
            var claimVersion = intent.Version;
            var attempt = NotificationDeliveryAttempt.Create(intent.Id, intent.AttemptCount, now);
            db.NotificationDeliveryAttempts.Add(attempt);
            claims.Add(new NotificationClaim(intent, attempt, ownerToken, claimVersion));
        }


        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claims;
    }

    private async Task ProcessAsync(
        VolunteerCoordinatorDbContext db,
        NotificationClaim claim,
        IEmailTemplateRenderer renderer,
        ITransactionalEmailProvider provider,
        EmailOptions emailOptions,
        ITokenService tokens,
        ITransientLinkMaterialStore transient,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await IsClaimCurrentAsync(db, claim, cancellationToken))
            {
                return;
            }

            var now = _clock.UtcNow;
            var volunteer = await db.Volunteers.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == claim.Intent.VolunteerId,
                cancellationToken);
            if (volunteer is null || volunteer.AnonymizedAtUtc.HasValue)
            {
                await CompleteAsync(
                    db,
                    claim,
                    now,
                    new ProviderDeliveryResult(false, false, "ContactRemoved"),
                    cancellationToken);
                return;
            }

            var settings = await db.GroupSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            var slot = claim.Intent.ShiftSlotId is Guid slotId
                ? await db.ShiftSlots.AsNoTracking().SingleOrDefaultAsync(
                    x => x.Id == slotId,
                    cancellationToken)
                : null;
            var shift = slot is null
                ? null
                : await db.Shifts.AsNoTracking().SingleOrDefaultAsync(
                    x => x.Id == slot.ShiftId,
                    cancellationToken);
            if ((slot is null || shift is null) &&
                IsRecurringNotificationKind(claim.Intent.Kind))
            {
                (slot, shift) = await LoadRecurringNotificationContextAsync(
                    db,
                    claim.Intent,
                    volunteer.Id,
                    cancellationToken);
            }

            if (settings is null || slot is null || shift is null)
            {
                await CompleteAsync(
                    db,
                    claim,
                    now,
                    new ProviderDeliveryResult(false, false, "ContextUnavailable"),
                    cancellationToken);
                return;
            }

            var assignment = await db.Assignments.AsNoTracking()
                .Where(x => x.ShiftSlotId == slot.Id && x.VolunteerId == volunteer.Id)
                .OrderByDescending(x => x.AssignedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var request = await db.ShiftRequests.AsNoTracking()
                .Where(x => x.ShiftSlotId == slot.Id && x.VolunteerId == volunteer.Id)
                .OrderByDescending(x => x.RequestedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var status = assignment?.Status.ToString() ?? request?.Status.ToString() ?? "Pending";
            string? privateUrl = null;
            if (IsRecoveryDeliveryKind(claim.Intent.Kind))
            {
                privateUrl = await CreateRecoveryUrlAsync(
                    db,
                    claim,
                    volunteer.Id,
                    slot.Id,
                    tokens,
                    emailOptions,
                    cancellationToken);
            }
            else if (IsRecurringCapabilityLinkKind(claim.Intent.Kind))
            {
                privateUrl = await CreateRecurringCapabilityUrlAsync(
                    db,
                    claim,
                    volunteer.Id,
                    tokens,
                    emailOptions,
                    transient,
                    cancellationToken);
            }
            else if (IsCapabilityLinkKind(claim.Intent.Kind))
            {
                privateUrl = await CreateCapabilityUrlAsync(
                    db,
                    claim,
                    volunteer.Id,
                    slot.Id,
                    tokens,
                    emailOptions,
                    transient,
                    cancellationToken);
            }
            else if (!await IsClaimCurrentAsync(db, claim, cancellationToken))
            {
                return;
            }

            if (!await IsClaimCurrentAsync(db, claim, cancellationToken))
            {
                return;
            }

            var template = renderer.Render(
                new NotificationTemplateContext(
                    volunteer.Name,
                    shift.Title,
                    slot.Kind == Domain.Schedules.SlotKind.Primary ? "Primary" : $"Backup {slot.Position}",
                    shift.StartsAtUtc,
                    shift.EndsAtUtc,
                    settings.TimeZoneId,
                    shift.Location,
                    shift.VolunteerInstructions,
                    status,
                    privateUrl,
                    emailOptions.ReplyTo),
                claim.Intent.Kind);
            var result = await provider.SendAsync(
                volunteer.Email,
                emailOptions.From ?? string.Empty,
                emailOptions.ReplyTo ?? string.Empty,
                claim.Attempt.IdempotencyKey,
                template,
                cancellationToken);
            if (!result.Accepted && result.Transient)
            {
                // An exact in-memory payload may safely reuse the same provider
                // attempt and idempotency key before durable retry scheduling.
                result = await provider.SendAsync(
                    volunteer.Email,
                    emailOptions.From ?? string.Empty,
                    emailOptions.ReplyTo ?? string.Empty,
                    claim.Attempt.IdempotencyKey,
                    template,
                    cancellationToken);
            }

            await CompleteAsync(db, claim, _clock.UtcNow, result, cancellationToken);
        }
        catch (ClaimNoLongerCurrentException)
        {
            // Cancellation, reassignment, anonymization, or a terminal webhook
            // won while this claim was being materialized or delivered.
            await MarkClaimSupersededAsync(db, claim, cancellationToken);
        }
    }

    private async Task<string> CreateRecoveryUrlAsync(
        VolunteerCoordinatorDbContext db,
        NotificationClaim claim,
        Guid volunteerId,
        Guid slotId,
        ITokenService tokens,
        EmailOptions emailOptions,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockSlotAsync(db, slotId, cancellationToken);
        db.ChangeTracker.Clear();
        var intent = await LoadIntentForUpdateAsync(db, claim.Intent.Id, cancellationToken)
            ?? throw new ClaimNoLongerCurrentException();
        EnsureClaimCurrent(intent, claim);
        if (!await IsEligibleAccessIntentAsync(
                db,
                intent,
                volunteerId,
                slotId,
                _clock.UtcNow,
                cancellationToken))
        {
            intent.Cancel(_clock.UtcNow, "CommitmentNoLongerEligible");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new ClaimNoLongerCurrentException();
        }

        if (intent.RecoveryTokenId is Guid existingId)
        {
            var existing = await db.RecoveryTokens.SingleOrDefaultAsync(
                x => x.Id == existingId,
                cancellationToken);
            existing?.Invalidate(_clock.UtcNow);
        }

        var generated = tokens.Generate();
        var recovery = RecoveryToken.Create(volunteerId, slotId, generated.Hash, _clock.UtcNow);
        db.RecoveryTokens.Add(recovery);
        intent.SetRecoveryToken(recovery.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BuildUrl(emailOptions.PublicBaseUrl, "/Commitments/Recover/", generated.RawToken);
    }

    private async Task<string> CreateRecurringCapabilityUrlAsync(
        VolunteerCoordinatorDbContext db,
        NotificationClaim claim,
        Guid volunteerId,
        ITokenService tokens,
        EmailOptions emailOptions,
        ITransientLinkMaterialStore transient,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var intent = await LoadIntentForUpdateAsync(db, claim.Intent.Id, cancellationToken)
            ?? throw new ClaimNoLongerCurrentException();
        EnsureClaimCurrent(intent, claim);
        var isRequest = string.Equals(
            intent.Kind,
            "RecurringCommitmentRequest",
            StringComparison.Ordinal);
        var isCommitment = string.Equals(
            intent.Kind,
            "RecurringCommitmentAccess",
            StringComparison.Ordinal) ||
            string.Equals(
                intent.Kind,
                "RecurringCommitmentConfirmation",
                StringComparison.Ordinal);
        if (!isRequest && !isCommitment)
        {
            throw new ClaimNoLongerCurrentException();
        }

        if (isRequest)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "RecurringCommitmentRequests" WHERE "Id" = {intent.TransitionId} FOR UPDATE""",
                cancellationToken);
        }
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "RecurringCommitments" WHERE "Id" = {intent.TransitionId} FOR UPDATE""",
                cancellationToken);
        }
        var request = isRequest
            ? await db.RecurringCommitmentRequests.SingleOrDefaultAsync(
                x => x.Id == intent.TransitionId,
                cancellationToken)
            : null;
        var commitment = isCommitment
            ? await db.RecurringCommitments.SingleOrDefaultAsync(
                x => x.Id == intent.TransitionId,
                cancellationToken)
            : null;
        if (request is null && commitment is null ||
            request?.VolunteerId != volunteerId &&
            commitment?.VolunteerId != volunteerId)
        {
            throw new ClaimNoLongerCurrentException();
        }

        var activeCapabilities = await db.RecurringCommitmentCapabilities
            .Where(x =>
                x.VolunteerId == volunteerId &&
                x.InvalidatedAtUtc == null &&
                (isRequest
                    ? x.RequestId == intent.TransitionId
                    : x.CommitmentId == intent.TransitionId))
            .ToListAsync(cancellationToken);
        var rawToken = transient.TryTakeHubToken(
            intent.Id,
            _clock.UtcNow,
            out var transientRawToken)
            ? transientRawToken
            : null;
        var generated = rawToken is null ? tokens.Generate() : null;
        var raw = rawToken ?? generated!.RawToken;
        if (generated is not null)
        {
            foreach (var capability in activeCapabilities)
            {
                capability.Invalidate(_clock.UtcNow);
            }

            db.RecurringCommitmentCapabilities.Add(
                RecurringCommitmentCapability.Create(
                    volunteerId,
                    isRequest ? intent.TransitionId : null,
                    isCommitment ? intent.TransitionId : null,
                    generated.Hash,
                    _clock.UtcNow));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BuildUrl(emailOptions.PublicBaseUrl, "/Recurring/Hub/", raw);
    }

    private async Task<string> CreateCapabilityUrlAsync(
        VolunteerCoordinatorDbContext db,
        NotificationClaim claim,
        Guid volunteerId,
        Guid slotId,
        ITokenService tokens,
        EmailOptions emailOptions,
        ITransientLinkMaterialStore transient,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockSlotAsync(db, slotId, cancellationToken);
        db.ChangeTracker.Clear();
        var intent = await LoadIntentForUpdateAsync(db, claim.Intent.Id, cancellationToken)
            ?? throw new ClaimNoLongerCurrentException();
        EnsureClaimCurrent(intent, claim);
        if (!await IsEligibleAccessIntentAsync(
                db,
                intent,
                volunteerId,
                slotId,
                _clock.UtcNow,
                cancellationToken))
        {
            intent.Cancel(_clock.UtcNow, "CommitmentNoLongerEligible");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new ClaimNoLongerCurrentException();
        }

        if (string.Equals(intent.Kind, "RequestReceipt", StringComparison.Ordinal) &&
            transient.TryTakeHubToken(intent.Id, _clock.UtcNow, out var rawToken) &&
            rawToken is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return BuildUrl(emailOptions.PublicBaseUrl, "/Requests/Status/", rawToken);
        }

        if (string.Equals(intent.Kind, "RequestReceipt", StringComparison.Ordinal))
        {
            // A restarted process cannot recover the browser's raw hub value.
            // A fresh scoped recovery token leaves the browser's existing hub
            // valid and gives delivery a durable, single-use replacement.
            var generatedRecovery = tokens.Generate();
            var recovery = RecoveryToken.Create(
                volunteerId,
                slotId,
                generatedRecovery.Hash,
                _clock.UtcNow);
            db.RecoveryTokens.Add(recovery);
            intent.SetRecoveryToken(recovery.Id);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return BuildUrl(
                emailOptions.PublicBaseUrl,
                "/Commitments/Recover/",
                generatedRecovery.RawToken);
        }

        var capabilities = await db.VolunteerAccessCapabilities
            .Where(x => x.VolunteerId == volunteerId &&
                        x.ShiftSlotId == slotId &&
                        x.InvalidatedAtUtc == null)
            .ToListAsync(cancellationToken);
        var generated = tokens.Generate();
        foreach (var capability in capabilities)
        {
            capability.Invalidate(_clock.UtcNow);
        }

        db.VolunteerAccessCapabilities.Add(
            VolunteerAccessCapability.Create(
                slotId,
                volunteerId,
                generated.Hash,
                _clock.UtcNow,
                CapabilityIssuedReasonFor(intent.Kind)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BuildUrl(emailOptions.PublicBaseUrl, "/Requests/Status/", generated.RawToken);
    }
    private async Task MarkClaimSupersededAsync(
        VolunteerCoordinatorDbContext db,
        NotificationClaim claim,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.ChangeTracker.Clear();
        var intent = await LoadIntentForUpdateAsync(db, claim.Intent.Id, cancellationToken);
        var attempt = await db.NotificationDeliveryAttempts.SingleOrDefaultAsync(
            x => x.Id == claim.Attempt.Id,
            cancellationToken);
        if (intent is not null && attempt?.CompletedAtUtc is null)
        {
            attempt!.Complete(
                _clock.UtcNow,
                intent.State == NotificationIntentState.Cancelled
                    ? "ClaimCancelled"
                    : "ClaimSuperseded");
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }


    private async Task CompleteAsync(
        VolunteerCoordinatorDbContext db,
        NotificationClaim claim,
        DateTimeOffset now,
        ProviderDeliveryResult result,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var intent = await LoadIntentForUpdateAsync(db, claim.Intent.Id, cancellationToken)
            ?? throw new ClaimNoLongerCurrentException();
        var attempt = await db.NotificationDeliveryAttempts.SingleAsync(
            x => x.Id == claim.Attempt.Id,
            cancellationToken);
        if (!IsClaimCurrentForSideEffects(intent, claim))
        {
            if (attempt.CompletedAtUtc is null)
            {
                attempt.Complete(
                    now,
                    intent.State == NotificationIntentState.Cancelled
                        ? "ClaimCancelled"
                        : "ClaimSuperseded");
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (attempt.CompletedAtUtc is null)
        {
            if (result.Accepted)
            {
                attempt.Complete(now, "Accepted", result.ProviderMessageId);
                intent.Accept(now, result.ProviderMessageId ?? "accepted");
            }
            else
            {
                attempt.Complete(now, result.Category);
                if (result.Transient && claim.Attempt.Ordinal < RetryOffsets.Length)
                {
                    var next = intent.CreatedAtUtc.Add(RetryOffsets[claim.Attempt.Ordinal]);
                    if (result.RetryAfterUtc is DateTimeOffset retryAfter)
                    {
                        next = retryAfter.ToUniversalTime() > next
                            ? retryAfter.ToUniversalTime()
                            : next;
                        var ceiling = intent.CreatedAtUtc.Add(RetryOffsets[claim.Attempt.Ordinal]);
                        if (next > ceiling)
                        {
                            next = ceiling;
                        }
                    }

                    intent.ScheduleRetry(now, next, result.Category);
                }
                else
                {
                    if (IsClaimCurrentForSideEffects(intent, claim) &&
                        intent.RecoveryTokenId is Guid recoveryTokenId)
                    {
                        var recovery = await db.RecoveryTokens.SingleOrDefaultAsync(
                            x => x.Id == recoveryTokenId,
                            cancellationToken);
                        recovery?.Invalidate(now);
                        intent.ClearRecoveryToken();
                    }

                    intent.Fail(now, result.Category);
                }
            }
        }


        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> IsClaimCurrentAsync(
        VolunteerCoordinatorDbContext db,
        NotificationClaim claim,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var intent = await db.NotificationIntents.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == claim.Intent.Id,
            cancellationToken);
        return intent is not null && IsClaimCurrentForSideEffects(intent, claim);
    }

    private static async Task<NotificationIntent?> LoadIntentForUpdateAsync(
        VolunteerCoordinatorDbContext db,
        Guid intentId,
        CancellationToken cancellationToken) =>
        await db.NotificationIntents
            .FromSqlInterpolated(
                $"""SELECT * FROM "NotificationIntents" WHERE "Id" = {intentId} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);

    private static async Task LockSlotAsync(
        VolunteerCoordinatorDbContext db,
        Guid slotId,
        CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "ShiftSlots" WHERE "Id" = {slotId} FOR UPDATE""",
            cancellationToken);

    private static void EnsureClaimCurrent(
        NotificationIntent intent,
        NotificationClaim claim)
    {
        if (!IsClaimCurrentForSideEffects(intent, claim))
        {
            throw new ClaimNoLongerCurrentException();
        }
    }

    private static bool IsClaimCurrentForSideEffects(
        NotificationIntent intent,
        NotificationClaim claim) =>
        intent.IsClaimOwned(claim.ClaimOwnerToken) &&
        intent.Version >= claim.ClaimVersion;

    private static async Task<bool> IsEligibleAccessIntentAsync(
        VolunteerCoordinatorDbContext db,
        NotificationIntent intent,
        Guid volunteerId,
        Guid slotId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var volunteer = await db.Volunteers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == volunteerId,
            cancellationToken);
        if (IsRecurringCapabilityLinkKind(intent.Kind))
        {
            if (volunteer is null || volunteer.AnonymizedAtUtc.HasValue)
            {
                return false;
            }

            if (string.Equals(intent.Kind, "RecurringCommitmentRequest", StringComparison.Ordinal))
            {
                var request = await db.RecurringCommitmentRequests.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == intent.TransitionId, cancellationToken);
                return request is not null &&
                       request.VolunteerId == volunteerId &&
                       request.Status is RecurringCommitmentRequestStatus.Pending or
                           RecurringCommitmentRequestStatus.Approved;
            }

            var commitment = await db.RecurringCommitments.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == intent.TransitionId, cancellationToken);
            return commitment is not null &&
                   commitment.VolunteerId == volunteerId &&
                   commitment.State is RecurringCommitmentState.AwaitingConfirmation or
                       RecurringCommitmentState.Active;
        }

        var slot = await db.ShiftSlots.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == slotId,
            cancellationToken);
        var shift = slot is null
            ? null
            : await db.Shifts.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == slot.ShiftId,
                cancellationToken);
        if (volunteer is null ||
            volunteer.AnonymizedAtUtc.HasValue ||
            slot is null ||
            !slot.IsActive ||
            shift is null ||
            !shift.IsActive ||
            now > shift.EndsAtUtc.AddDays(7))
        {
            return false;
        }

        var activeAssignment = await db.Assignments.AsNoTracking()
            .Where(x => x.ShiftSlotId == slotId &&
                        x.VolunteerId == volunteerId &&
                        (x.Status == AssignmentStatus.Assigned ||
                         x.Status == AssignmentStatus.Confirmed))
            .OrderByDescending(x => x.AssignedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var latestRequest = await db.ShiftRequests.AsNoTracking()
            .Where(x => x.ShiftSlotId == slotId && x.VolunteerId == volunteerId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(intent.Kind, "AssignmentAccess", StringComparison.Ordinal) ||
            string.Equals(intent.Kind, "CoordinatorAccessReissue", StringComparison.Ordinal))
        {
            return activeAssignment is not null &&
                   activeAssignment.Id == intent.TransitionId;
        }

        if (string.Equals(intent.Kind, "RequestReceipt", StringComparison.Ordinal))
        {
            var request = latestRequest?.Id == intent.TransitionId
                ? latestRequest
                : await db.ShiftRequests.AsNoTracking().SingleOrDefaultAsync(
                    x => x.Id == intent.TransitionId,
                    cancellationToken);
            var sourceAssignment = request is null
                ? null
                : await db.Assignments.AsNoTracking().SingleOrDefaultAsync(
                    x => x.SourceRequestId == request.Id &&
                         (x.Status == AssignmentStatus.Assigned ||
                          x.Status == AssignmentStatus.Confirmed),

                    cancellationToken);
            return (request?.VolunteerId == volunteerId &&
                    request.ShiftSlotId == slotId &&
                    request.Status == RequestStatus.Pending) ||
                   sourceAssignment is not null;
        }

        return activeAssignment is not null || latestRequest?.Status == RequestStatus.Pending;
    }
    private static async Task<(ShiftSlot? Slot, Shift? Shift)> LoadRecurringNotificationContextAsync(
        VolunteerCoordinatorDbContext db,
        NotificationIntent intent,
        Guid volunteerId,
        CancellationToken cancellationToken)
    {
        Guid seriesId;
        SlotKind roleKind;
        int rolePosition;
        DateOnly effectiveLocalDate;
        DateOnly endLocalDate;
        if (string.Equals(intent.Kind, "RecurringCommitmentRequest", StringComparison.Ordinal))
        {
            var request = await db.RecurringCommitmentRequests.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == intent.TransitionId, cancellationToken);
            if (request is null || request.VolunteerId != volunteerId)
            {
                return (null, null);
            }

            seriesId = request.SeriesId;
            roleKind = request.RoleKind;
            rolePosition = request.RolePosition;
            effectiveLocalDate = request.EffectiveLocalDate;
            endLocalDate = request.EndLocalDate;
        }
        else
        {
            var commitment = await db.RecurringCommitments.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == intent.TransitionId, cancellationToken);
            if (commitment is null || commitment.VolunteerId != volunteerId)
            {
                return (null, null);
            }

            seriesId = commitment.SeriesId;
            roleKind = commitment.RoleKind;
            rolePosition = commitment.RolePosition;
            effectiveLocalDate = commitment.EffectiveLocalDate;
            endLocalDate = commitment.EndLocalDate;
        }

        var occurrences = await db.RecurringShiftOccurrences.AsNoTracking()
            .Where(x =>
                x.SeriesId == seriesId &&
                x.LocalDate >= effectiveLocalDate &&
                x.LocalDate <= endLocalDate &&
                x.Status == RecurringOccurrenceStatus.Generated &&
                x.ShiftId.HasValue &&
                !x.IsException)
            .OrderBy(x => x.LocalDate)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var occurrence in occurrences)
        {
            var shift = await db.Shifts.AsNoTracking()
                .Include(x => x.Slots)
                .SingleOrDefaultAsync(x => x.Id == occurrence.ShiftId!.Value, cancellationToken);
            var slot = shift?.Slots.FirstOrDefault(x =>
                x.IsActive &&
                x.Kind == roleKind &&
                x.Position == rolePosition);
            if (shift is not null && slot is not null)
            {
                return (slot, shift);
            }
        }

        return (null, null);
    }

    private static bool IsRecurringNotificationKind(string kind) =>
        kind.StartsWith("RecurringCommitment", StringComparison.Ordinal);

    private static bool IsRecurringCapabilityLinkKind(string kind) =>
        string.Equals(kind, "RecurringCommitmentRequest", StringComparison.Ordinal) ||
        string.Equals(kind, "RecurringCommitmentAccess", StringComparison.Ordinal) ||
        string.Equals(kind, "RecurringCommitmentConfirmation", StringComparison.Ordinal);

    private static bool IsRecoveryDeliveryKind(string kind) =>
        string.Equals(kind, "AccessRecovery", StringComparison.Ordinal) ||
        string.Equals(kind, "CoordinatorAccessReissue", StringComparison.Ordinal);

    private static bool IsCapabilityLinkKind(string kind) =>
        string.Equals(kind, "RequestReceipt", StringComparison.Ordinal) ||
        string.Equals(kind, "AssignmentAccess", StringComparison.Ordinal);

    private static CapabilityIssuedReason CapabilityIssuedReasonFor(string kind) =>
        string.Equals(kind, "RequestReceipt", StringComparison.Ordinal)
            ? CapabilityIssuedReason.Request
            : CapabilityIssuedReason.DirectAssignment;

    private static string BuildUrl(string? configuredBaseUrl, string path, string token)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? "https://localhost"
            : configuredBaseUrl.TrimEnd('/');
        return $"{baseUrl}{path}{Uri.EscapeDataString(token)}";
    }
}
