# Requirements: Protect Live Workflow State During Coordinator Exceptions

**Issue**: #13
**Date**: 2026-09-02
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As a** volunteer coordinator
**I want** deactivation and assignment correction to resolve every related workflow record atomically
**So that** the schedule never hides commitments that volunteers can still act upon

---

## Background

Shift deactivation currently marks only the shift inactive. Active assignments, pending requests, and unused action tokens remain live after the shift disappears from public openings and coverage. Coordinators also cannot directly cancel an active assignment, and the coordinator request queue reports a slot as available without considering its active assignment. PostgreSQL remains authoritative, coordinator actions remain authenticated and audited, and volunteer action links remain expiring and single-use.

The approved deactivation policy resolves rather than blocks: one transaction locks the shift's slots, supersedes every pending request, cancels every active assignment, invalidates every unused action token for those assignments, deactivates the shift, and records the coordinator action. A failed or conflicting operation rolls back the whole transition.

---

## Acceptance Criteria

### AC1: Safe shift deactivation

**Given** an active shift has pending requests, active assignments, or unused assignment action tokens
**When** an authenticated coordinator deactivates it with the current expected version
**Then** the shift is deactivated, pending requests are superseded, active assignments are cancelled, their unused action tokens become unusable, and the coordinator action is audited in one PostgreSQL transaction

### AC2: Coordinator cancellation

**Given** a slot has an assigned or confirmed assignment
**When** an authenticated coordinator cancels that assignment
**Then** the assignment becomes cancelled, the slot becomes uncovered, every unused action token for the assignment becomes unusable, and the coordinator identity is audited atomically

### AC3: Consistent coordinator state

**Given** a request targets a published future slot with an active assignment
**When** the coordinator views requests and coverage
**Then** both surfaces report the slot as Unconfirmed for an assigned assignment or Confirmed for a confirmed assignment rather than Available

### AC4: Concurrent correction

**Given** separate PostgreSQL transactions attempt conflicting assignment, volunteer-action, coordinator-cancellation, or shift-deactivation transitions for the same slot
**When** the transactions commit
**Then** row locking, concurrency tokens, and active-state uniqueness constraints produce one serially valid authoritative result, reject stale conflicting writes, and leave no pending request, active assignment, or usable action token stranded behind an inactive shift

---

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Explain deactivation consequences on the schedule page, then deactivate a shift by locking its slots in deterministic order and atomically superseding pending requests, cancelling active assignments, invalidating their unused action tokens, deactivating the shift, and auditing the coordinator. | Must | Preserve the existing expected-version check. |
| FR2 | Add an authenticated coordinator command that cancels an assigned or confirmed assignment, reopens its slot, invalidates every unused action token for that assignment, and records `AssignmentCancelledByCoordinator`. | Must | No page-local state mutation. |
| FR3 | Keep raw action tokens out of persistence and make every previously issued token fail without mutation after coordinator cancellation, shift deactivation, or coordinator reassignment ends its assignment. | Must | Reuse `ActionToken.Invalidate`. |
| FR4 | Make coordinator request projection assignment-aware: `Assigned` maps to `Unconfirmed`, `Confirmed` maps to `Confirmed`, and neither state is approvable or labelled `Available`. | Must | Reuse the coverage vocabulary. |
| FR5 | Preserve PostgreSQL as the transaction authority, serialize assignment creation/ending/action/link generation through slot-row locks, and retain the existing assignment/request/token concurrency tokens plus partial unique indexes for active assignments and pending requests. | Must | No schema migration is required. |
| FR6 | Add behavior-focused xUnit coverage for atomic deactivation, direct coordinator cancellation, administrative token invalidation including reassignment, request/coverage projection consistency, hidden-shift regression, and conflicting PostgreSQL transitions. | Must | Use isolated PostgreSQL for persistence behavior. |

---

## Out of Scope

- Recurring schedules
- Notification delivery for coordinator cancellation or deactivation
- Volunteer self-assignment policy
- New assignment, request, token, or audit database columns

---

## Versioning

The `enhancement` label requires a minor version bump. Update root `VERSION` from `0.3.0` to `0.4.0` during implementation.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #13 | 2026-09-02 | Initial feature spec |
