# Design: Create a Durable Accountless Volunteer Action Hub

**Issue**: #18
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Overview

Replace separate coordinator-generated assignment actions with one commitment-scoped bearer hub while preserving accountless access, hash-at-rest, PostgreSQL authority, and notification-independent workflow transitions. Keep the existing `/Requests/Status/{token}` route as the canonical hub route so already-distributed request-status URLs require no redirect or alias. The page title and content become `Your commitment` rather than a request-only view.

The reusable hub capability authorizes status reads. A mutation is single-effect because Application hashes and revalidates the capability after the slot lock, then applies the existing assignment transition with status concurrency. The hub token is not consumed by a successful action, allowing the same link to show the resulting terminal state. Replays encounter the new state and commit no second transition.

Prerequisites: #14 provides tiered anonymous rate limiting, #15 provides group-local complete commitment context, and #19 provides durable email intent/attempt delivery. Integrate #13 administrative invalidation and #16 privacy removal when present.

---

## Capability Model and Migration

Add `VolunteerAccessCapability` with:

- `Guid Id`, `Guid ShiftSlotId`, `Guid VolunteerId`;
- required 32-byte `TokenHash` with unique index;
- `DateTimeOffset CreatedAtUtc`;
- nullable `InvalidatedAtUtc`, concurrency token;
- `string IssuedReason` (`Request`, `DirectAssignment`, `Recovery`, `CoordinatorReissue`), persisted as bounded text or enum according to existing domain conventions.

A partial unique index permits one active capability per `(ShiftSlotId, VolunteerId)`. Replacement invalidates the current row before inserting a new one in the same transaction. Capability validity additionally requires an active shift/slot association and `nowUtc <= Shift.EndsAtUtc + 7 days`; it is therefore automatically extended/shortened by an authorized shift edit without rewriting capability rows. Administrative deactivation and privacy removal explicitly invalidate capability/recovery rows despite the read grace.

Migrate each existing `ShiftRequest.StatusTokenHash` into a capability for its volunteer/slot. If historical duplicate requests exist, the newest request keeps an active capability and older copied rows are invalidated during migration. Remove status-token hash/expiry columns and query paths from `ShiftRequest` after data movement. The unchanged `/Requests/Status/{token}` route hashes against the capability table, so the newest distributed request link remains usable.

Add `RecoveryToken` with capability/volunteer/slot identity, unique 32-byte hash, created/expiry/used/invalidated UTC fields and concurrency. Expiry is exactly 30 minutes. Add a recovery-delivery intent record or extend #19's durable notification intent with non-secret template data: volunteer ID, slot ID, reason, and correlation ID. No raw hub/recovery token or rendered link is stored.

---

## Hub Read and Action Flow

### Read

`InspectCommitmentHubAsync(rawToken)` hashes the token, performs a scalar preflight to locate the immutable slot ID, locks nothing for a read, and projects capability plus current request, assignment, shift, slot, volunteer, and #15 `CommitmentDto`. It returns one generic invalid/expired result for unknown, invalidated, inactive/deactivated, anonymized, or grace-expired access.

Valid hubs display current request/assignment state and complete local context. Actions are derived server-side:

| Current state and time | Offered action |
|------------------------|----------------|
| Request pending/resolved without active assignment | None |
| Assignment `Assigned`, `nowUtc < StartsAtUtc` | Confirm, Decline |
| Assignment `Confirmed`, `nowUtc < EndsAtUtc` | Cancel |
| Terminal assignment, start/end deadline reached | None; plain-language read-only status |

Use strict `<` mutation deadlines; equality is closed. Read access remains through `EndsAtUtc + 7 days` inclusive. The page is `Cache-Control: no-store`, sets `Referrer-Policy: no-referrer`, has no third-party resources, renders raw token only in the same-origin route/form action, and never logs route values.

### Mutation

`ApplyHubActionAsync(rawCapability, action, coordinator-independent cancellationToken)`:

1. hashes the raw token and obtains its slot ID through a scalar no-tracking projection;
2. locks the slot before tracking mutable state;
3. reloads/revalidates active capability, non-anonymized volunteer, active shift/slot, action deadline, and authoritative assignment;
4. calls existing `Assignment.Confirm`, `Decline`, or `Cancel` only when offered;
5. records the existing volunteer-token audit with capability ID only as an entity identifier, never hash/raw token;
6. commits once, then records notification intent independently through #19.

The anonymous POST remains antiforgery-protected. Concurrent actions, deactivation, reassignment, privacy removal, capability replacement, and shift edits serialize through slot locks plus existing concurrency tokens. The losing request returns the current hub state or generic invalid result and never reports whether another bearer won.

---

## Capability Issuance

Request submission generates one random raw hub token, persists only its hash with the request in the existing transaction, and returns/distributes the same `/Requests/Status/{raw}` link. Approval for the same volunteer/slot retains that capability.

New direct assignment and reassignment to another volunteer generate a capability in the assignment transaction and return the raw link only to the post-commit #19 notification call. If delivery fails, the assignment stays committed, access delivery is visibly failed, and the raw value is discarded. Coordinator reissue creates another delivery intent; no raw link is persisted or shown in routine coordinator UI.

A reassignment to a new volunteer invalidates the old volunteer's hub in the same transaction under #13. Shift deactivation invalidates all related hubs and recovery tokens. #16 anonymization does the same before removing contact data.

---

## Accountless Recovery

Map `GET|POST /Commitments/Recover`. The form accepts email and commitment date in the configured group local zone. Normalize email exactly as the volunteer model does. Apply #14's strict anonymous recovery tier (default 5 POSTs per client IP per 15 minutes) in addition to ordinary form validation.

Every syntactically valid POST redirects to one receipt page with exactly:

`If those details match an eligible commitment, a recovery email will arrive shortly. The current link remains valid until replacement access is completed.`

Do not vary status, timing intentionally beyond ordinary bounded database work, body, or redirect for zero/one/many matches. Find non-anonymized volunteers by normalized email and commitments whose group-local start date equals the posted date and whose hub grace has not expired. Enqueue one separately scoped recovery delivery intent per match, bounded to three; deduplicate an equivalent pending intent. The caller never sees the count.

### Delivery and redemption

Issue #19's worker claims an intent and generates a fresh random raw recovery token immediately before provider delivery. It stores only the hash/30-minute expiry, sends the raw `/Commitments/Recover/{raw}` URL transiently, and invalidates an unsent token before any retry generates another. A crash may cause a duplicate email, but all tokens remain individually short-lived and single-use; raw material is absent from persistence/logs.

Redeeming a valid recovery token locks the slot, reloads eligibility, consumes the token, invalidates every active hub for that volunteer/slot, creates one new capability/hash, and returns a redirect containing the new raw hub token after commit. If the transaction fails, no token/capability state changes. Unknown, expired, invalidated, used, anonymized, deactivated, or replayed recovery tokens show the same invalid result.

Because old hubs remain active until successful redemption, knowing email/date cannot lock a volunteer out. Successful redemption is the only anonymous replacement boundary.

---

## Coordinator Visibility and Reissue

Extend coverage/assignment details with `Volunteer access` state: `Active`, `Delivery pending`, `Message not sent`, `Expired`, or `Revoked`. State comes from capability plus latest #19 intent/attempt and never from page-local flags.

Provide two authenticated antiforgery flows with server-rendered consequence previews:

- **Send replacement access** — queues coordinator reissue; current hubs remain valid until recovery redemption.
- **Revoke now and send replacement** — warns that current access stops immediately and delivery failure leaves no usable link; final transaction invalidates active hubs/recovery tokens, audits `VolunteerAccessRevokedByCoordinator`, then queues delivery independently.

Both record `VolunteerAccessReissueRequested` with normalized coordinator, volunteer/slot IDs, mode, and notification correlation only. Delivery success/failure is separately visible and audited by #19. Repeated pending reissue is deduplicated. A coordinator may retry failed delivery without extending a recovery token; the worker creates a fresh 30-minute token.

Remove the coordinator `Generate action links` workflow and stop adding new `ActionToken` rows. Replace its navigation with access state/reissue.

---

## Legacy Action-Link Compatibility Exception

The user approved a bounded transitional exception to the normal clean-cutover rule. Existing `/Actions/{token}` links created before deployment continue through their stored `ExpiresAtUtc`, at most seven days. No API/UI path creates or extends an `ActionToken` after cutover. The legacy handler retains existing hash, slot-lock, action, expiry, concurrency, and generic-error behavior and gains the new state/time deadlines.

After expiry, every old link is uniformly unusable. The table remains historical until a later schema migration can remove it safely, but production code may query it only from the legacy route; all current hub/reissue/recovery code uses capabilities. Instrument no raw token and expose no “legacy” wording to volunteers.

This exception avoids breaking already-delivered links while guaranteeing that the second mutation path drains naturally in seven days. Tests fix pre/post-expiry clocks and prove no post-cutover token generation.

---

## Security and Privacy

- Capabilities are least-authority per commitment, never volunteer-wide.
- Raw hub/recovery tokens are cryptographically random, transient, redacted from logging, and represented at rest only by SHA-256 hashes.
- Generic invalid and recovery-receipt responses reveal no email, date, commitment, capability, or token match.
- Recovery is rate-limited, deduplicated, bounded, and cannot invalidate current access before bearer redemption.
- Hub/recovery pages use no-store/no-referrer and existing antiforgery on POST.
- Coordinator reissue/revoke requires allowlist authorization, explicit consequences, identity normalization, and audit.
- Privacy anonymization invalidates access; no recovery delivery targets anonymized contact.

---

## Performance Considerations

Unique hash and active `(slot, volunteer)` indexes make hub lookup/replacement bounded. Recovery email/date queries use normalized-email and shift-time indexes, cap matches at three, and deduplicate pending intents. Hub reads load one commitment graph; coordinator coverage batches access/delivery state rather than querying per row.

No polling or volunteer session store is introduced. Seven-day grace is evaluated from authoritative shift data.

---

## Testing Strategy

| Layer | Type | Coverage |
|-------|------|----------|
| Domain/Application | Unit | Capability/recovery validity boundaries, offered actions, replacement/revoke transitions, generic outcomes. |
| PostgreSQL | Integration | Status-token migration, active uniqueness, slot-lock conflicts, concurrent action/redemption/revoke/deactivate/anonymize, one committed result, token absence. |
| Web | Integration/browser | Same hub before/after assignment, long-future access, context, action deadlines, generic recovery, no-store/no-referrer, antiforgery, coordinator state/reissue. |
| Delivery | Integration with #19 adapter | Recovery/direct-assignment transient token delivery, failure visibility, retry token replacement, no raw persistence/logs. |
| Legacy | Fixed-time integration | Pre-cutover links work only until stored expiry; no new token generation; post-expiry replay changes nothing. |

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #18 | 2026-09-03 | Initial feature spec |
