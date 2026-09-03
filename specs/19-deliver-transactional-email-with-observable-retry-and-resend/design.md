# Design: Deliver Transactional Email With Observable Retry and Resend

**Issue**: #19
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Overview

Use a transactional outbox inside the existing PostgreSQL database and an in-process hosted service inside the existing Web deployment. Workflow commands insert a non-secret `NotificationIntent` in the same transaction as domain state and audit. The worker later resolves current recipient/context, renders an ephemeral message, calls Resend, and persists delivery outcome separately. Provider failure therefore never rolls back a committed schedule transition.

Resend is selected because its official .NET SDK supports provider idempotency keys retained for 24 hours and signed webhook verification is documented. Accepted API response means only that Resend accepted the message. Signed events distinguish delivery to the receiving mail server, bounce, and complaint; the UI never claims the person read the message.

Issue #15 is required for group-local complete context. Issue #18 and this issue should be executed together: core outbox/provider work can precede the hub, while link-bearing templates depend on #18's transient recovery/capability material contract. #16 privacy removal cancels pending intent and removes contact without copying it into delivery rows.

---

## Notification Domain and Persistence

Replace the current single `NotificationAttempt` model with two focused aggregates/tables.

### `NotificationIntent`

- `Guid Id` and unique bounded `EventKey` for business-level deduplication;
- `Guid TransitionId`, `Guid VolunteerId`, nullable `Guid ShiftSlotId`;
- `NotificationKind Kind`;
- `NotificationState State`: `Pending`, `RetryScheduled`, `InFlight`, `Accepted`, `Delivered`, `Bounced`, `Complained`, `Failed`, `Cancelled`;
- `DateTimeOffset CreatedAtUtc`, `NextAttemptAtUtc`, nullable lease/accepted/delivered/completed timestamps;
- `int AttemptCount` and `uint Version`/mapped concurrency token;
- nullable bounded provider message ID and safe final-failure category.

`EventKey` is derived from event kind plus authoritative transition/correlation identity, never email or token. Recipient is only `VolunteerId`; the worker resolves current non-anonymized email immediately before each attempt. Store no subject/body/destination/raw webhook or access URL.

Terminal states are Delivered, Bounced, Complained, Failed, and Cancelled. Accepted remains webhook-awaiting. A delivery webhook may advance Accepted to Delivered/Bounced/Complained. Bounced/Complained override Accepted and late Delivered; events older than an already-applied terminal provider event do not regress state.

### `NotificationDeliveryAttempt`

Each scheduled provider submission records `Id`, `NotificationIntentId`, ordinal 1-5, start/completion time, outcome category, Resend idempotency key derived from attempt ID, and nullable provider message ID. It stores no payload/destination. Unique `(IntentId, Ordinal)` and idempotency-key constraints prevent duplicate application attempts.

Migrate existing unavailable-adapter rows as terminal historical failures. Resolve a volunteer ID through transition/request/assignment where possible, discard the duplicated destination during migration, retain safe kind/timing/outcome, and never resend migrated history automatically.

### Webhook receipt

`ResendWebhookReceipt` stores only unique `svix-id`, event type, provider message ID, provider occurrence UTC, and processed UTC. It never stores raw payload, recipient, headers, bounce text, or secret. Unique event ID makes Resend's at-least-once webhook delivery idempotent.

---

## Intent Creation

Application commands add intent through `IWorkflowStore.AddNotificationIntent` before their transaction commits. Business event keys make command retry idempotent. Add intent for:

- request receipt;
- request approval/rejection;
- direct assignment/reassignment and #18 hub access;
- #18 account recovery/reissue;
- volunteer Confirm/Decline/Cancel outcome where a useful receipt is defined;
- coordinator cancellation and shift deactivation from #13;
- shift edits that change volunteer-visible title, start/end, location, or instructions for pending requests or active assignments.

Do not notify for internal-note-only edits, audit reads, list reads, or unchanged form submission. A correction creates one intent per affected volunteer/slot, deduplicated by shift version and kind. Deactivation/cancellation intent creation is part of the same authoritative transaction but does not restore a state transition if later delivery fails.

Intent insertion failure is a database failure and rolls back the workflow because otherwise the system would silently lose the obligation. Once intent exists, every provider attempt commits independently.

---

## Template and Link Material

Add an Application `IEmailTemplateRenderer` contract and Infrastructure implementation with fixed typed templates. Each emits UTF-8 plain text and conservative HTML with no remote image, tracking link, script, style injection, or coordinator-only notes. HTML-encode every title, name, location, instruction, and label; validate From/Reply-To from configuration rather than user input.

Subjects contain no private token and minimal context, for example `Your volunteer request`, `Your service commitment`, or `Volunteer schedule update`. Bodies identify why the message was sent, include #15 group-local start/end/zone, location, slot, volunteer instructions, visible current status, expiry/consequence text, and configured reply-to.

The trusted `Email:PublicBaseUrl` is an absolute HTTPS production origin. Never derive private links from `Host` or forwarded headers.

For #18 link-bearing intents, the worker asks `INotificationLinkMaterialFactory` for a fresh short-lived single-use delivery/recovery token immediately before provider submission. It persists only the token hash in #18 storage and keeps the raw URL in memory for rendering/sending. If the provider attempt is definitively rejected or abandoned after a lease/process failure, invalidate that token before creating another. Existing hub access remains governed by #18 and is never embedded from persistence.

Non-link templates can be deterministically regenerated. Link-bearing attempts cannot be reconstructed after process loss because raw values are intentionally absent. Crash recovery therefore starts a new application attempt/idempotency key and new token; an ambiguously delivered older email may exist but its abandoned token is invalidated. This is at-least-once email with single-effect workflow/access redemption, not an impossible exactly-once claim.

---

## Resend Adapter

Configure and validate in Production:

- `Resend:ApiKey` secret;
- `Resend:WebhookSecret` secret;
- `Email:From` verified sender address/name;
- `Email:ReplyTo` monitored group address;
- `Email:PublicBaseUrl` HTTPS origin.

Register the official `Resend` .NET package and typed client. Send plain and HTML bodies with one recipient. Set `Idempotency-Key` to `notification/{NotificationDeliveryAttempt.Id:N}`. Reuse a key only for an immediate/in-memory retry of the exact same payload and only within Resend's documented 24-hour window. A different token/payload always receives a new attempt ID/key.

Classify timeout, connection failure, HTTP 408, 409 concurrent-idempotent request, 429, and 5xx as transient. Honor provider `Retry-After` only within the approved next-attempt ceiling. Treat invalid payload/idempotency conflict, unauthorized/forbidden, unprocessable recipient, and other non-transient 4xx as permanent safe categories. Persist no response body. Logs use intent/attempt/provider-message IDs and safe category only.

The unavailable adapter remains selectable only outside Production through explicit configuration for deterministic development; Production with incomplete Resend/email settings fails startup rather than reporting messages sent.

---

## Worker, Leases, and Retry

`NotificationDeliveryHostedService` starts after Web composition and uses `PeriodicTimer`, fresh scopes, and cancellation. Poll every 15 seconds and claim at most 25 due intents ordered by `NextAttemptAtUtc`, then creation/ID. PostgreSQL claim uses `FOR UPDATE SKIP LOCKED`, sets state InFlight and a two-minute lease, increments attempt count, inserts the uniquely numbered attempt, and commits before provider I/O.

Five total scheduled attempt times relative to initial due time are: immediately, +1 minute, +5 minutes, +30 minutes, +120 minutes. Do not add delays cumulatively beyond those absolute offsets. A transient failure schedules the next approved offset. After ordinal five, state becomes Failed. A permanent failure becomes Failed immediately. Coordinator resend creates a new intent/event key and its own five-attempt budget; it does not reset history.

An expired lease is reclaimable. For a non-link payload, the worker may reconstruct and retry the same provider attempt/key only if exact content identity is guaranteed. For secret-bearing or uncertain content, mark the abandoned attempt safe category `LeaseAbandoned`, invalidate its link token, and create the next ordinal with fresh material/key. Never hold a PostgreSQL transaction open during Resend network I/O.

Successful API response stores provider message ID and Accepted. It does not schedule another send. If no webhook arrives, UI remains `Accepted by email service`; it never guesses Delivered.

---

## Signed Resend Webhook

Map anonymous `POST /webhooks/resend` outside Razor Pages. Read a bounded raw request body. Require `svix-id`, `svix-timestamp`, and `svix-signature`; use the configured Resend/Svix verification API with timestamp tolerance before JSON parsing. Invalid/missing/oversized requests return 400 and mutate nothing.

Handle `email.delivered`, `email.bounced`, and `email.complained`. Locate by provider message ID, insert receipt and advance state in one transaction. Duplicate `svix-id` returns 200 without another transition. Unknown provider IDs are acknowledged and recorded only as an ID/type receipt or ignored according to SDK contract, never logged with recipient/payload. Unsubscribed event types return 200 without mutation. Apply provider occurrence time and precedence so late/out-of-order Delivered cannot overwrite Bounced/Complained.

Return 200 only after a verified supported/ignored event is safely processed. The webhook has no antiforgery requirement because signature verification is its authentication. Apply request-size limits and never log the raw body or headers.

---

## Coordinator Messages and Resend

Build `/Coordinator/Messages` using #17 plain-language navigation where available. Show commitment/person context, kind, created time, next retry/attempt count, and one of: `Waiting to send`, `Trying again`, `Accepted by email service`, `Delivered to mail server`, `Bounced`, `Recipient reported spam`, `Not sent`, `Cancelled`. Explain that Delivered is mail-server acceptance, not proof of reading.

Expand a row to show safe attempt ordinal/times/categories, never raw provider response or token. Actions:

- `Send another copy` for ordinary non-access context creates a new audited intent.
- `Send replacement access` and `Revoke now and send replacement` delegate to #18's consequence/reissue commands.
- Pending/Retrying intents cannot be duplicated; offer status only.

Every POST is coordinator-authorized, antiforgery-protected, reloads state, records `NotificationResendRequested` or #18's access audit, and redirects with textual outcome. Provider calls remain worker-owned and never occur in the page request.

---

## Security and Privacy

- Resend/API/webhook secrets exist only in environment/Railway secret variables and are redacted by configuration/logging.
- Recipient email stays on `Volunteer`; notification rows refer by ID. #16 anonymization cancels pending/retrying intent before clearing contact.
- Raw links exist only in process memory between generation and provider submission; hashes are stored by #18.
- Template fields are encoded; public base URL is trusted configuration; no raw HTML from schedule data.
- Webhook verification occurs before parsing; receipts are deduplicated and raw input is discarded.
- Coordinator pages expose operational context only to allowlisted users and never show credentials, hashes, raw URLs, or provider bodies.

---

## Performance Considerations

Due-intent indexes cover `(State, NextAttemptAtUtc)`; provider-message and webhook-event IDs are unique. `SKIP LOCKED` and batches of 25 avoid duplicate claims and long locks. Context/recipient loads are batched per claimed set; no query per template field.

Provider I/O is outside transactions. Polling at 15 seconds is sufficient for transactional email without another process. Webhook handling performs indexed lookups and bounded body reads.

---

## Testing Strategy

| Layer | Type | Coverage |
|-------|------|----------|
| Application | Unit | Event-key dedup, template selection/encoding, classification, absolute retry schedule, monotonic state transitions. |
| PostgreSQL | Integration | Intent atomicity, claim/lease/`SKIP LOCKED`, unique attempts, crash recovery, cancellation/anonymization, concurrent workers, workflow independence. |
| Adapter contract | Stub HTTP/Resend sandbox | Headers/idempotency, payload, transient/permanent mapping, no secret logging, safe exact-payload retry. |
| Webhook | Signed fixtures | Raw-body verification, missing/invalid/duplicate/out-of-order delivered/bounced/complained events, no raw persistence. |
| Web/browser | Integration | Plain state/history, authorization/antiforgery, resend dedup/audit, link delivery/redemption with #18. |

Provider sandbox verification uses only reserved/test recipients and confirms accepted plus webhook transitions. Do not send production email from the automated suite.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #19 | 2026-09-03 | Initial feature spec |
