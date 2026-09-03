# Tasks: Support Policy-Based Self-Scheduling and Recurring Service Commitments

**Issue**: #21
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Policy | 2 | [ ] |
| Recurring participation | 3 | [ ] |
| Presentation | 1 | [ ] |
| Verification | 2 | [ ] |
| **Total** | 8 | |

---

## Prerequisites

Issues #14, #16, #17, #18, #19, and #20 supply abuse, privacy, guided interaction, capabilities, delivery, and recurring occurrences respectively.

---

## Phase 1: Policy

### T001: Persist explicit scheduling policy

**File(s)**: Shift/series Domain types; Application commands/DTOs; EF mappings; new migration; coordinator shift/series forms
**Type**: Create / Modify
**Depends**: #20
**Acceptance**:
- [ ] whole-shift/series ApprovalRequired or DirectClaim policy is required and all existing/new records default ApprovalRequired
- [ ] plain cards distinguish coordinator review from immediate confirmation and identify Direct claim as lower maintenance
- [ ] published policy changes require expected version, preview, audit, and no pending-request bypass
- [ ] series changes create effective revisions and do not alter existing recurring requests/commitments

### T002: Enforce one-time direct claim atomically

**File(s)**: Assignment direct-claim transition; `VolunteerCoordinatorService`; `IWorkflowStore`; `EfWorkflowStore`; public opening/request pages
**Type**: Modify
**Depends**: T001, #14, #16, #18, #19
**Acceptance**:
- [ ] visible policy controls Send request versus Claim commitment
- [ ] direct claim locks slot/volunteer and atomically creates Confirmed assignment, capability, intent, and audit
- [ ] two separate-context claimers produce one winner and clear loser with no partial volunteer/request/access state
- [ ] approval-required workflow remains unchanged except shared presentation

---

## Phase 2: Recurring Participation

### T003: Model bounded recurring requests and commitments

**File(s)**: new recurring request/commitment/join/capability Domain models; EF/store mappings; new migration
**Type**: Create / Modify
**Depends**: T001, #18, #20
**Acceptance**:
- [ ] 4–26 week finite range/default 12, source policy/request, role, state, and concrete occurrence/assignment traceability are persisted
- [ ] PostgreSQL exclusion prevents overlapping AwaitingConfirmation/Active commitments for one series role/date range
- [ ] unique joins/capability hashes enforce idempotent materialization and scoped access
- [ ] SkippedException/Replaced/Withdrawn history remains visible and never overwrites occurrence state

### T004: Implement recurring claim, approval, and one confirmation

**File(s)**: recurring Application commands/projections/store methods; public recurring pages; coordinator Requests integration
**Type**: Create / Modify
**Depends**: T003, #14, #16, #19
**Acceptance**:
- [ ] server preview re-enumerates exact Included/Skipped generated published dates and requires at least one eligible occurrence
- [ ] DirectClaim creates Active commitment plus Confirmed assignments atomically
- [ ] ApprovalRequired creates one request; approval creates AwaitingConfirmation plus Assigned assignments and supersedes conflicts atomically
- [ ] one recurring-hub action confirms every still-eligible cadence assignment and activates commitment; replay changes nothing
- [ ] deterministic slot locks plus exclusion/active uniqueness resolve concurrent claims/approvals serially

### T005: Reconcile withdrawal, exceptions, and revision handoff

**File(s)**: recurring materializer/worker integration; hub withdrawal; assignment exception hooks; #20 handoff commands; coverage/notification integration
**Type**: Modify / Create
**Depends**: T004
**Acceptance**:
- [ ] new eligible occurrences materialize idempotently with commitment state; unavailable exceptions remain labelled/skipped
- [ ] effective-date withdrawal cancels all and only later cadence assignments/access in one transaction while earlier history remains
- [ ] individual volunteer cancellation or coordinator replacement marks one join/occurrence exception and later cadence continues
- [ ] series correction keeps old assignments by default; explicit fresh handoff atomically maps selected old/new rows or applies nothing
- [ ] coverage, hubs, #17 attention, #19 intent, and audit reflect every committed result

---

## Phase 3: Presentation

### T006: Add understandable one-time and cadence flows

**File(s)**: public opening/recurring/hub Razor Pages; coordinator policy/request/coverage/series pages; shared #17 components; CSS
**Type**: Create / Modify
**Depends**: T002, T004, T005
**Acceptance**:
- [ ] policy consequence is visible immediately before contact fields and submit label matches request/claim
- [ ] recurring flow separates role, first date/horizon, exact-date review, and submit outcome with 12 weeks preselected
- [ ] recurring hub shows request/confirmation/active/withdrawn status, Included/Skipped occurrences, one confirmation, and future withdrawal
- [ ] coordinator sees cadence/exceptions/substitute/handoff without raw IDs or recurrence jargon
- [ ] 320px/keyboard/assistive use preserves labels, order, state, errors, confirmation, and no color/gesture dependency

---

## Phase 4: Verification

### T007: Add policy and recurring concurrency coverage

**File(s)**: `tests/VolunteerCoordinator.UnitTests/`; `tests/VolunteerCoordinator.IntegrationTests/`
**Type**: Create / Modify
**Depends**: T001, T002, T003, T004, T005
**Acceptance**:
- [ ] tests cover safe migration/default, policy change blockers, direct confirmation, exact horizon/range, exceptions, and zero-eligible rejection
- [ ] held-lock PostgreSQL races cover direct claims, overlapping recurring claims, approval versus claim, confirmation, withdrawal, top-up, substitution, and handoff
- [ ] every loser/stale/replay path commits no partial volunteer/request/assignment/capability/intent/audit state
- [ ] cross-feature tests cover anonymization, deactivation, access invalidation, delivery failure independence, and occurrence protection

### T008: Verify actual self-scheduling journeys and version

**File(s)**: Web integration/browser tests; actual Web surface; `VERSION`
**Type**: Modify / Verify
**Depends**: T006, T007
**Acceptance**:
- [ ] increment the implementation branch's current root version by one minor component
- [ ] browser completes one-time request/direct-claim race and recurring direct/approval/one-confirmation journeys using only visible text
- [ ] browser withdraws from a chosen occurrence, replaces one occurrence, and explicitly hands a cadence across revision while other history remains
- [ ] actual hub/email/coverage/attention states and mobile/keyboard semantics agree with PostgreSQL
- [ ] formatting, Release build, full isolated-PostgreSQL suite, migrations, workers, and end-to-end email/Web smoke pass

---

## Dependency Graph

```text
T001 ──▶ T002 ───────────────┐
  │                          │
  └──▶ T003 ──▶ T004 ──▶ T005 ──▶ T006 ──▶ T008
       │          │       │          │
       └──────────┴───────┴──▶ T007 ─┘
```

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #21 | 2026-09-03 | Initial feature spec |
