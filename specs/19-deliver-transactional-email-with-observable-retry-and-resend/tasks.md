# Tasks: Deliver Transactional Email With Observable Retry and Resend

**Issue**: #19
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Outbox | 2 | [ ] |
| Delivery | 2 | [ ] |
| Operations | 1 | [ ] |
| Verification | 2 | [ ] |
| **Total** | 7 | |

---

## Prerequisites

- Issue #15 supplies group-local commitment context.
- Execute issue #18 jointly: core outbox/provider work precedes hub integration; link-bearing templates consume #18's transient material contract.
- Integrate issue #16 anonymization and issue #17 coordinator vocabulary when present.

---

## Phase 1: Outbox

### T001: Replace unavailable attempts with durable intent state

**File(s)**: Domain notification intent/attempt/state types; `VolunteerCoordinatorDbContext`; `IWorkflowStore`; `EfWorkflowStore`; new EF Core migration
**Type**: Create / Modify
**Depends**: None
**Acceptance**:
- [ ] intent stores event/transition/volunteer/slot identity, lifecycle, lease, retry, provider ID, and safe categories without destination/body/raw link
- [ ] delivery attempts have unique intent ordinal and provider idempotency key
- [ ] webhook receipts deduplicate provider event IDs without raw payload/recipient
- [ ] existing unavailable history migrates terminally with destination discarded and is never automatically resent
- [ ] due/provider/event indexes and concurrency mappings support bounded workers

### T002: Insert intents atomically with workflow transitions

**File(s)**: `VolunteerCoordinatorService`; relevant Domain/Application models and store contract
**Type**: Modify
**Depends**: T001, #15
**Acceptance**:
- [ ] request, decision, assignment, action, cancellation/deactivation, access, and volunteer-visible correction commands insert deduplicated intent in their workflow transaction
- [ ] internal-note/unchanged edits create no intent
- [ ] intent database failure rolls back the workflow; every later provider outcome is independent
- [ ] one correction intent per affected volunteer/slot is batch-created without provider I/O
- [ ] business event keys contain no contact or token material

---

## Phase 2: Delivery

### T003: Implement safe typed templates and Resend adapter

**File(s)**: Application email/template ports and models; Infrastructure Resend adapter/renderer; dependency registration; runtime configuration
**Type**: Create / Modify
**Depends**: T002, #15, #18
**Acceptance**:
- [ ] official Resend .NET SDK sends configured verified From/Reply-To and trusted HTTPS-base links
- [ ] request, assignment/access, recovery/reissue, decision, correction, cancellation, and deactivation have encoded plain-text and HTML templates
- [ ] group-local complete context appears; coordinator notes, secrets, tracking content, and user HTML do not
- [ ] link factory generates raw #18 token only in memory and persists only its hash
- [ ] transient/permanent response classification stores safe categories and no body/credential

### T004: Run leased bounded notification delivery

**File(s)**: notification delivery hosted service; store claim/lease operations; Web composition/options
**Type**: Create / Modify
**Depends**: T001, T003
**Acceptance**:
- [ ] worker claims ordered batches of 25 with `FOR UPDATE SKIP LOCKED`, two-minute leases, fresh scopes, cancellation, and no transaction during network I/O
- [ ] five total attempts occur at absolute offsets 0, 1, 5, 30, and 120 minutes; permanent/fifth failure ends retry
- [ ] exact in-memory payload retry reuses its 24-hour idempotency key; fresh material gets a new attempt/key
- [ ] abandoned secret-bearing attempt invalidates its token before fresh retry and never duplicates workflow/access redemption
- [ ] API success records Accepted/provider ID and waits for webhook rather than claiming delivery

---

## Phase 3: Operations

### T005: Process webhooks and expose coordinator recovery

**File(s)**: `/webhooks/resend` endpoint; signature verifier; `/Coordinator/Messages` page; #18 reissue/revoke pages; operations/configuration documentation
**Type**: Create / Modify
**Depends**: T004, #17, #18
**Acceptance**:
- [ ] bounded raw-body Svix verification precedes parsing; invalid requests mutate nothing
- [ ] duplicate/out-of-order delivered/bounced/complained events advance state monotonically and return correct acknowledgements
- [ ] coordinator list shows plain current state and safe attempt history, distinguishing Accepted from Delivered
- [ ] ordinary resend and #18 normal/revoke-now access reissue are deduplicated, antiforgery-protected, and audited without provider I/O in request
- [ ] Production fails startup when Resend, sender, reply-to, public HTTPS base, or webhook settings are incomplete

---

## Phase 4: Verification

### T006: Add outbox, provider, and webhook coverage

**File(s)**: `tests/VolunteerCoordinator.UnitTests/`; `tests/VolunteerCoordinator.IntegrationTests/`; fake provider/webhook fixtures
**Type**: Create / Modify
**Depends**: T001, T002, T003, T004, T005
**Acceptance**:
- [ ] tests cover every event/template, encoded hostile content, event dedup, correction filtering, and recipient resolution
- [ ] PostgreSQL tests prove workflow+intent atomicity, provider-failure independence, lease races, fixed retry schedule, final failure, and concurrent worker safety
- [ ] adapter tests assert idempotency/authorization headers and classification without leaking request/response bodies
- [ ] signed webhook tests cover invalid/missing/duplicate/out-of-order delivered/bounced/complained events
- [ ] database/log scans find no API/webhook secret, destination duplicate, rendered body, or raw capability/recovery URL

### T007: Verify real delivery operations and version

**File(s)**: actual Web/provider sandbox; `VERSION`
**Type**: Modify / Verify
**Depends**: T006
**Acceptance**:
- [ ] increment the implementation branch's current root version by one minor component
- [ ] sandbox recipient receives each safe template with correct local context and #18 link redemption
- [ ] forced timeout, 429, 5xx, permanent rejection, bounce, complaint, and resend show the approved state/retry/audit behavior while workflow stays committed
- [ ] actual coordinator Messages UI and webhook endpoint satisfy authorization/signature boundaries without secret output
- [ ] formatting, Release build, full isolated-PostgreSQL suite, migrations, Docker/Compose, and Resend sandbox smoke pass

---

## Dependency Graph

```text
T001 ──▶ T002 ──▶ T003 ──▶ T004 ──▶ T005 ──▶ T006 ──▶ T007
                 ▲                    ▲
#15 ─────────────┘                    │
#18 ──────────────────────────────────┘
```

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #19 | 2026-09-03 | Initial feature spec |
