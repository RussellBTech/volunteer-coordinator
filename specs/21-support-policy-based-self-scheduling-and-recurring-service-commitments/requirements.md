# Requirements: Support Policy-Based Self-Scheduling and Recurring Service Commitments

**Issue**: #21
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As a** volunteer
**I want** to claim either one opening or an ongoing service cadence under the group's scheduling policy
**So that** regular service does not require repeating the same request every week

---

## Background

Every public submission currently becomes a pending request requiring coordinator approval. Recurring issue #20 produces independent concrete occurrences but no recurring participation, direct claim, future withdrawal, or occurrence-level substitute/handoff behavior.

The approved policy is explicit per whole standalone shift or recurring series, applying to all its primary/backup roles. Existing and newly created records default to `Approval required`; coordinators may deliberately choose `Direct claim`, which is explained as lower maintenance because an eligible volunteer becomes confirmed immediately. A recurring commitment selects one role and a concrete 4–26 week range, default 12. Direct claim atomically confirms eligible occurrences. Approval creates assigned occurrences and one recurring-hub confirmation confirms the cadence once.

Series corrections preserve old recurring assignments as protected exceptions until a coordinator explicitly previews and confirms handoff. Individual substitutions do not end the surrounding recurring commitment.

---

## Acceptance Criteria

### AC1: Explicit scheduling policy

**Given** a coordinator reviews a standalone shift or recurring series
**When** they choose Approval required or Direct claim
**Then** plain-language consequences are previewed, the one policy applies to every role, existing/new records default safely to Approval required, and public forms show/enforce the persisted policy atomically

### AC2: One-time direct claim

**Given** an open published Direct claim slot
**When** two eligible volunteers claim it concurrently
**Then** exactly one PostgreSQL transaction creates a Confirmed assignment, commitment capability, notification intent, and audit, while the other returns `This commitment was just claimed. Choose another opening.` with no partial volunteer/workflow state

### AC3: Bounded recurring commitment

**Given** a published recurring series accepts ongoing volunteers
**When** a volunteer selects a role, effective occurrence, and 4–26 week horizon
**Then** a Direct claim creates confirmed assignments, or Approval required creates one coordinator request whose approval creates assigned occurrences and one hub confirmation confirms the cadence, without overwriting filled/manual/Needs-review/skipped exceptions

### AC4: Future withdrawal

**Given** a volunteer holds an active recurring commitment
**When** they withdraw through the recurring hub effective from a selected future occurrence
**Then** all later eligible recurring assignments cancel atomically, individual access becomes unusable, earlier history and occurrence exceptions remain, future slots reopen, coverage risk and notification intent become visible, and replay changes nothing

### AC5: Coordinator exception and series handoff

**Given** one occurrence needs a substitute or a series revision changes future dates
**When** a coordinator replaces one occurrence or explicitly hands a recurring volunteer to revised occurrences
**Then** a one-occurrence replacement remains an exception while other cadence assignments continue, and revision handoff previews/cancels/creates only the selected future set with accurate hubs, notifications, coverage, and audits

### AC6: Understandable signup choice

**Given** a coordinator or volunteer does not know scheduling-system terminology
**When** policy and cadence choices are displayed on desktop, mobile, keyboard, or assistive technology
**Then** concrete outcomes explain `A coordinator reviews each request` versus `The first eligible volunteer is confirmed immediately`, identify Direct claim as lower maintenance while Approval required remains the safe preselected default, and show exact included/skipped dates before submission

---

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Add `SignupPolicy` (`ApprovalRequired`, `DirectClaim`) to each standalone Shift and recurring-series revision; one value governs all its roles. Migrate/default all records to ApprovalRequired. | Must | Policy changes use coordinator preview/audit. |
| FR2 | For one-time DirectClaim, lock the slot then atomically create/update eligible volunteer, Confirmed assignment, #18 capability, #19 intent, and audit. | Must | Existing active/pending conflicts revalidated; first writer wins. |
| FR3 | Add recurring request/commitment/occurrence-materialization models with series, volunteer, chosen slot role, effective/end local dates, state, source policy/request, and traceable concrete assignment links. | Must | No persistent volunteer account. |
| FR4 | Allow recurring horizons of 4–26 weeks, default 12, only across existing generated published occurrences. Preserve exception/occupied/review/skipped rows and show them before submission. | Must | At least one eligible occurrence required. |
| FR5 | Direct recurring claim creates Confirmed concrete assignments. Approval-required submission waits for coordinator; approval creates Assigned occurrences and one recurring capability action confirms all still-eligible cadence assignments atomically. | Must | No per-week confirmation. |
| FR6 | Prevent overlapping active recurring commitments for the same series role/date range at PostgreSQL/application boundaries, and serialize concrete assignment materialization through deterministic slot locks and existing active uniqueness. | Must | No oversubscription under concurrency. |
| FR7 | Add a hashed recurring-commitment hub capability for status, one-time cadence confirmation, and effective-date withdrawal. Individual occurrence hub cancellation/replacement marks only that occurrence as an exception. | Must | #18 security/recovery/no-store rules apply. |
| FR8 | Withdraw future cadence atomically and idempotently; preserve earlier history, cancel future assignments, invalidate related hubs/tokens, create #19 notifications, and surface reopened coverage. | Must | Strict effective local occurrence boundary. |
| FR9 | Keep protected recurring assignments on old #20 occurrences by default after a series revision. Add an explicit coordinator handoff preview to map eligible future assignments to revised occurrences; no automatic move. | Must | Stale/blocked handoff applies nothing. |
| FR10 | Add guided public/coordinator, domain, PostgreSQL, and concurrency coverage for policy defaults/changes, direct-claim races, recurring approval/confirmation, exceptions, withdrawal, handoff, hub, email, and accessible copy. | Must | Fixed clocks/zones; isolated PostgreSQL. |

---

## Out of Scope

- Persistent volunteer accounts, qualifications, certifications, ranking, or automatic matching
- A recurring commitment longer than 26 weeks or without a concrete end
- Automatic movement of protected assignments after series correction
- Multiple policies for different roles inside one shift/series
- Monthly recurrence or external calendar integration

---

## Versioning

The `enhancement` label requires one minor version increment from the implementation branch's current root `VERSION`.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #21 | 2026-09-03 | Initial feature spec |
