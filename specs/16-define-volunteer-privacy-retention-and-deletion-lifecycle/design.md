# Design: Define Volunteer Privacy Retention and Deletion Lifecycle

**Issue**: #16
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Overview

Preserve relational workflow history by anonymizing eligible `Volunteer` aggregates in place rather than cascading deletion. One Application command owns both automatic retention and coordinator-assisted removal. It locks the volunteer row, recomputes eligibility from PostgreSQL, invalidates every private link, redacts notification destinations, transitions the volunteer, writes one minimal audit, and commits once.

A bounded in-process hosted service supplies automatic execution. Page models never mutate persistence directly. Existing coordinator authentication/allowlisting and PostgreSQL transactions remain the authority. The policy retains anonymized non-identifying workflow/audit records indefinitely and does not claim legal certification.

---

## Data Classification and Retention

Update product and technical steering with this approved table:

| Data | Classification | Current-store lifecycle |
|------|----------------|-------------------------|
| Volunteer name, email, normalized email, phone | Confidential identifying contact | While a live dependency exists, then through 365 elapsed UTC days after the retention anchor; earlier verified removal allowed. |
| Notification destination | Confidential identifying contact | Redacted with the owning volunteer's contact data. |
| Raw status/action links | Secret bearer credentials | Returned transiently, never persisted; previously issued links invalidated on anonymization. |
| Status/action token hashes | Sensitive authentication metadata | Retained only as invalid historical metadata after anonymization. |
| Volunteer surrogate ID and request/assignment/status/timestamps | Non-identifying operational history after anonymization | Retained indefinitely. |
| Audit action, actor, entity ID, non-identifying counts | Integrity history | Retained indefinitely. Coordinator actor retention is unchanged. |
| Backups | Protected recovery copy | Existing backup rotation; restored environments rerun retention before use. |

The one-year period is exactly `TimeSpan.FromDays(365)` against UTC instants, not a calendar-year interpretation. Eligibility requires both `anchor <= nowUtc - 365 days` and no live dependency.

The anchor is the maximum of `Volunteer.UpdatedAtUtc`, related `ShiftRequest.RequestedAtUtc`/`ResolvedAtUtc`, related `Assignment.AssignedAtUtc`/`ConfirmedAtUtc`/`EndedAtUtc`, related `NotificationAttempt.CreatedAtUtc`/`CompletedAtUtc`, and `Shift.EndsAtUtc` for every related request or assignment. A future shift end therefore prevents automatic anonymization.

---

## Domain and Persistence Changes

### Volunteer tombstone

Add nullable `AnonymizedAtUtc` and `Volunteer.Anonymize(nowUtc)`. The transition:

- requires UTC and is idempotent only by returning false when already anonymized;
- sets `Name` to `Removed volunteer`;
- sets `Email` to `removed-{Volunteer.Id:N}@invalid.invalid` and `NormalizedEmail` to its uppercase form, using the reserved `.invalid` domain solely to preserve required/unique schema invariants;
- sets `Phone` to null and `UpdatedAtUtc`/`AnonymizedAtUtc` to `nowUtc`;
- makes `UpdateContact` reject with `Removed volunteer contact data cannot be restored.`

No original value, reversible encryption, keyed hash, or derived contact fingerprint is retained. Add an index on `AnonymizedAtUtc` for bounded candidate scanning. Existing surrogate foreign keys stay unchanged.

### Private links and notification attempts

Add `ShiftRequest.StatusTokenInvalidatedAtUtc` as a concurrency token. `InvalidateStatusToken(nowUtc)` sets it once, and status-token usability/query resolution requires it to be null. Preserve the existing random hash as unusable historical metadata; never create a replacement raw token.

Reuse `ActionToken.Invalidate(nowUtc)` for every unused action token tied to the volunteer's assignments. Add `NotificationAttempt.RedactDestination()` which replaces `Destination` with `removed` and is idempotent; delivery state, kind, timestamps, transition ID, and safe error summary remain.

The EF migration adds only `Volunteers.AnonymizedAtUtc`, `ShiftRequests.StatusTokenInvalidatedAtUtc`, their concurrency/index configuration, and no copy of personal values.

---

## Application and Store Changes

Add store operations for:

- scalar volunteer-ID lookup by normalized email without tracking;
- `LockVolunteerAsync(Guid)` using PostgreSQL `FOR UPDATE`;
- post-lock tracked volunteer load;
- bounded candidate IDs with `AnonymizedAtUtc IS NULL` and a coarse cutoff;
- related requests, assignments, shifts, action tokens, and notification attempts required to calculate/revalidate eligibility and redact data.

Collection methods short-circuit empty identifiers. Reads used for eligibility are tracked only after the volunteer lock. Order any secondary slot locks by ascending `Guid` when existing assignment/request commands require them.

Update request submission and coordinator direct assignment/reassignment contact lookup to locate the immutable volunteer ID, lock an existing volunteer, reload it, then update/use it. New email after anonymization no longer matches the tombstone and creates a new `Volunteer`. This shared lock prevents a stale tracked entity from restoring contact data while anonymization commits.

### `AnonymizeVolunteerAsync`

The private core command accepts volunteer ID, reason (`RetentionExpired` or `CoordinatorRequest`), actor, now UTC, and cancellation token. In one transaction it:

1. locks and loads the volunteer; an already-anonymized record is a successful no-op without a second audit;
2. loads all related authoritative state and computes the exact anchor;
3. rejects if any request is `Pending`, any assignment is `Assigned`/`Confirmed`, or any related shift ends in the future;
4. for automatic retention, rejects if the anchor is newer than the 365-day cutoff;
5. invalidates usable status/action tokens and redacts notification destinations;
6. anonymizes the volunteer;
7. writes `VolunteerAnonymized` on the volunteer entity with reason, request/assignment/token/notification counts, and the anchor, but no name, email, phone, destination, or token material;
8. commits once.

Coordinator removal deliberately bypasses only the age test, never live-dependency checks.

### Retention worker

Add `VolunteerRetentionOptions` with committed defaults `RetentionDays=365`, `SweepIntervalHours=24`, and `BatchSize=100`; validate all as positive and prevent a configured retention shorter than the approved 365 days. `VolunteerRetentionHostedService` runs one sweep after application startup and then every 24 hours using `PeriodicTimer`, a fresh dependency-injection scope per batch, `IClock`, and cancellation propagation.

Candidates are ordered by volunteer ID and processed in bounded transactions. Concurrent workers may select the same ID, but the volunteer row lock plus `AnonymizedAtUtc` recheck makes later workers no-op, producing one audit. One malformed candidate is logged by surrogate ID and does not stop later batches; logs never include contact fields.

---

## Coordinator-Assisted Removal

Add `/Coordinator/Privacy` under existing folder authorization. GET shows purpose and a form for normalized email. POST lookup returns generic `No matching volunteer removal record was found.` for absent or already-anonymized contacts.

For a match, load a bounded list of recent related commitments containing only what an authenticated coordinator needs: shift ID/title/local or UTC date according to the currently approved presentation contract, slot role, and request/assignment status. The coordinator must select the exact commitment described by the volunteer's out-of-band request. The final antiforgery-protected POST includes volunteer ID and selected shift ID; the service revalidates that relationship before evaluating eligibility.

Eligible confirmation calls the shared anonymization command with actor `RequireCoordinator(email)` and reason `CoordinatorRequest`. Ineligible results identify only the blocking category and count: pending requests, active assignments, or future commitments. No partial token/destination/contact mutation occurs. Success text is `Volunteer contact data removed. Non-identifying scheduling history was retained.`

This is verification of an out-of-band request, not authentication of a volunteer account. The page explains that the coordinator should not proceed when the supplied recent commitment does not match.

---

## Privacy Notice and Language

Add a public `/Privacy` Razor Page and reusable `_VolunteerPrivacyNotice.cshtml` partial. Render it before submit controls on public request and coordinator assignment/reassignment forms. The concise notice states:

`We use your name and email only to coordinate this service commitment. Phone is optional. Authorized coordinators can see these details. We remove identifying contact data one year after your latest related activity when no live commitment needs it, or sooner after a verified request to {Privacy:ContactEmail}. Protected backups expire through their normal rotation. This records service scheduling only; it does not indicate AA membership or attendance.`

Bind and validate `Privacy:ContactEmail` as a syntactically valid required address in Production. Development/Integration environments may use the committed non-delivery example `privacy@example.invalid`. The full page explains classification, retention anchor, early-removal process, blocked live dependencies, non-identifying history, backup rotation, and scope limits in plain language.

Search all public/coordinator copy introduced or touched by this issue. Use `volunteer`, `coordinator`, `service commitment`, and `scheduling`; never `AA member`, `membership`, or `attendee` as a characterization.

---

## Security, Concurrency, and Failure Handling

- Coordinator privacy routes require the existing policy and antiforgery; the public privacy page is read-only.
- Search uses exact normalized email, generic absent responses, and no query-string contact data.
- Anonymization, link invalidation, notification redaction, and audit commit in one PostgreSQL transaction. Concurrency failure rolls back all changes.
- Volunteer row locking is shared by contact reuse and anonymization. Existing slot locks and uniqueness constraints continue to serialize workflow state.
- Application logs include only volunteer surrogate ID, reason, counts, and outcome. Free-text provider errors remain bounded and must not contain destinations.
- Restores are isolated; retention runs before restored data is exposed or used for notification.

---

## Testing Strategy

| Layer | Type | Coverage |
|-------|------|----------|
| Domain | Unit | Optional phone remains null; tombstone values; no restoration; UTC/idempotency rules; status-token invalidation; destination redaction. |
| Application/PostgreSQL | Integration | Exact 365-day boundary and anchor sources, live-dependency blocks, immediate coordinator removal, atomic rollback, row-lock races, idempotent concurrent workers, all token/destination cleanup, one minimal audit. |
| Web | Integration/browser | Notice order/copy/link, required-vs-optional fields, generic search, recent-commitment verification, success/block feedback, authorization/antiforgery, neutral anonymized display, no membership claim. |
| Restore regression | PostgreSQL | Restored anonymized state remains anonymized; restored expired identifiable state is removed before serving/notification. |

Use fixed UTC instants. Persistence tests must assert original contact byte strings are absent from current `Volunteers`, `NotificationAttempts`, and `AuditEntries` after anonymization and that previously issued status/action links fail without mutation.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #16 | 2026-09-03 | Initial feature spec |
