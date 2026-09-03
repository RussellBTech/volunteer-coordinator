# Tasks: Create a Durable Accountless Volunteer Action Hub

**Issue**: #18
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Capability | 2 | [ ] |
| Recovery and delivery | 2 | [ ] |
| Coordinator and compatibility | 1 | [ ] |
| Verification | 2 | [ ] |
| **Total** | 7 | |

---

## Prerequisites

- Issue #14 supplies anonymous endpoint rate limits.
- Issue #15 supplies group-local complete commitment context.
- Issue #19 supplies real durable transactional-email delivery and transient link material handling.
- Integrate issue #13 administrative cancellation/deactivation and issue #16 anonymization when present.

---

## Phase 1: Capability

### T001: Migrate request status into commitment capabilities

**File(s)**: new Domain capability/recovery types; request/application models; `VolunteerCoordinatorDbContext`; `IWorkflowStore`; `EfWorkflowStore`; new EF Core migration
**Type**: Create / Modify
**Depends**: #15
**Acceptance**:
- [ ] one active hashed capability is enforced per volunteer/slot and validity follows current shift end plus seven days
- [ ] newest existing status hashes migrate so distributed request URLs remain usable; older duplicate request capabilities are invalidated
- [ ] request status hash/expiry columns and paths are removed after data movement
- [ ] raw hub/recovery values never persist or enter logs/audits
- [ ] administrative deactivation/reassignment/anonymization invalidates related capability/recovery state atomically

### T002: Make the status page a state-aware action hub

**File(s)**: `VolunteerCoordinatorService`; hub models; `/Requests/Status/{token}` Page model/view; relevant shared commitment presentation
**Type**: Modify
**Depends**: T001, #13, #15
**Acceptance**:
- [ ] the same request link remains valid through assignment and seven days after current shift end
- [ ] complete local context and authoritative status render with no account
- [ ] Confirm/Decline appear only for Assigned before start; Cancel appears only for Confirmed before end
- [ ] action POST hashes/preflights, locks slot, reloads authoritative state, mutates/audits once, and replay changes nothing
- [ ] responses set no-store/no-referrer, POSTs use antiforgery, and invalid outcomes are generic

---

## Phase 2: Recovery and Delivery

### T003: Add non-enumerating accountless recovery

**File(s)**: recovery Application/store methods; `/Commitments/Recover` Razor Pages; #14 rate-limit configuration
**Type**: Create / Modify
**Depends**: T001, #14, #15
**Acceptance**:
- [ ] email plus group-local commitment date always yields one generic receipt for zero/one/many matches
- [ ] eligible matches are deduplicated, capped at three, and enqueue separately scoped non-secret delivery intents
- [ ] current hub access is unchanged by recovery request or delivery failure
- [ ] recovery POST default is five requests per client IP per 15 minutes
- [ ] caller-visible response and logs disclose no match count or contact/token value

### T004: Deliver and redeem single-use recovery access

**File(s)**: #19 notification intent/worker integration; recovery-token service/store; `/Commitments/Recover/{token}` Page
**Type**: Create / Modify
**Depends**: T003, #19
**Acceptance**:
- [ ] worker generates raw 30-minute recovery token only at delivery time and persists only its hash
- [ ] retry invalidates an unsent token and generates another; delivery status remains explicit
- [ ] redemption locks slot and atomically consumes recovery, invalidates all old hubs, creates one new hash, and redirects with the raw hub capability
- [ ] unknown/expired/used/replayed/deactivated/anonymized outcomes are indistinguishable and mutation-free
- [ ] crash/retry scenarios never persist or log raw link material

---

## Phase 3: Coordinator and Compatibility

### T005: Replace manual links with access operations

**File(s)**: coverage/assignment access projections and Razor Pages; coordinator reissue/revoke previews; legacy `/Actions/{token}` handler; remove current link-generation UI/command
**Type**: Modify
**Depends**: T002, T004
**Acceptance**:
- [ ] coverage shows Active, Delivery pending, Message not sent, Expired, or Revoked from authoritative state
- [ ] normal reissue keeps old hubs until redemption; explicit revoke-now invalidates immediately with consequence preview
- [ ] actor/mode/correlation are audited without tokens; delivery failure never rolls back assignment/access revocation
- [ ] no current path creates `ActionToken`; generate-links UI/command is removed
- [ ] previously issued action links alone remain valid through stored expiry with existing security and new time deadlines, then uniformly fail

---

## Phase 4: Verification

### T006: Add capability lifecycle and concurrency coverage

**File(s)**: `tests/VolunteerCoordinator.UnitTests/`; `tests/VolunteerCoordinator.IntegrationTests/`
**Type**: Create / Modify
**Depends**: T001, T002, T003, T004, T005
**Acceptance**:
- [ ] tests cover migration of latest/duplicate request status hashes, long-future validity, exact action/grace boundaries, shift edits, and terminal reads
- [ ] separate-context slot-lock tests cover competing hub actions, recovery redemption, reissue/revoke, deactivation, reassignment, and anonymization
- [ ] valid winner and losing/replay outcomes commit no duplicate workflow, capability, token, notification, or audit state
- [ ] database/log inspection finds no raw hub/recovery value
- [ ] fixed-time legacy tests prove stored-expiry-only acceptance and zero new action-token creation

### T007: Verify end-to-end hub and version enhancement

**File(s)**: Web/delivery integration tests; actual Web surface; `VERSION`
**Type**: Modify / Verify
**Depends**: T006
**Acceptance**:
- [ ] increment the implementation branch's current root version by one minor component
- [ ] browser flow uses one link from request through approval, confirmation, terminal status, and grace without account or manual action links
- [ ] recovery zero/match responses are identical; delivered token redeems once; old hub changes only on redemption
- [ ] direct assignment delivery and coordinator normal/revoke-now reissue expose correct success/failure state
- [ ] security headers, rate limit, antiforgery, local complete context, legacy drain, and authorization are verified on actual routes
- [ ] formatting, Release build, full isolated-PostgreSQL suite, migrations, and actual email-adapter/Web smoke scenario pass

---

## Dependency Graph

```text
#14 ───────▶ T003 ──┐
#15 ──▶ T001 ──▶ T002 ─┼──▶ T005 ──▶ T006 ──▶ T007
               │     │
#19 ───────────┴──▶ T004 ─┘
```

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #18 | 2026-09-03 | Initial feature spec |
