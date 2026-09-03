# Requirements: Give Maintainers an Efficient Stewardship Workspace

**Issue**: #22
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As a** rotating volunteer maintainer
**I want** prioritized work queues, reusable volunteer selection, understandable history, and safe handoff
**So that** the schedule remains operable without specialist knowledge

---

## Background

Issue #17 establishes the coordinator home, plain-language counts, safe actions, known/new assignment paths, and human history summaries. This issue scales those contracts for mature recurring operation: one prioritized work queue, bounded privacy-aware volunteer search instead of a full read-only roster, structured audit filtering/keyset pagination, and in-app handoff diagnostics backed by an explicit authenticated verification action.

The workspace remains operational, not analytical. `Soon` defaults to commitments starting within 72 hours; `Urgent` is within 24 hours. Volunteer search requires at least three characters and returns at most ten active, non-anonymized name/email matches. Audit pages contain 50 events using stable keyset cursors. Access diagnostics show normalized allowlisted emails and verification state but never manage OIDC or secrets.

---

## Acceptance Criteria

### AC1: Prioritized coordinator work

**Given** uncovered, soon-unconfirmed, failed-delivery, pending-decision, withdrawal, or recurrence-review work exists
**When** a coordinator signs in or opens Work
**Then** authoritative counts and a combined urgency-ordered queue lead directly to the narrow intervention, with urgent work inside 24 hours, soon work inside 72 hours, and no vanity metrics

### AC2: Safe roster reuse

**Given** a known volunteer is eligible for assignment
**When** a coordinator submits at least three name/email characters and selects one of at most ten matching results
**Then** the current stored name and full email are reused by server-side identifier without retyping, phone/history/nonmatching records are not disclosed, and anonymized or stale selections cannot be assigned

### AC3: Understandable audit history

**Given** a coordinator investigates a schedule or volunteer-reference change
**When** they filter by group-local date range, actor, shift, volunteer, or plain action category
**Then** stable 50-event keyset pages show human-readable local events with previous/next navigation, while raw JSON, GUIDs, tokens, secrets, and provider payloads remain hidden

### AC4: Safe maintainer handoff

**Given** coordinator responsibility changes
**When** the operational owner adds a second allowlisted OIDC identity and each coordinator explicitly verifies access in-app
**Then** Access shows distinct normalized addresses, configuration readiness, verification dates, current identity, and exact add-verify-remove order so the outgoing coordinator is not removed before independent access works

### AC5: Bounded set-based performance

**Given** representative recurring history with at least 1,000 volunteers, 5,000 concrete shifts, and 10,000 audits
**When** Home, Work, volunteer search, request/coverage, Messages, or History load at one and ten times page-result volume
**Then** SQL command counts remain within fixed page budgets and do not grow per rendered row, with indexes supporting urgency, search, filters, and keyset cursors

### AC6: Routine work without documentation

**Given** a rotating coordinator has no product training
**When** they handle today's requests, coverage risks, message failures, withdrawals, recurrence review, or access handoff
**Then** each task begins from Home/Work, presents one primary action, preserves valid input after errors, and ends with a plain-language next step using only application-visible guidance

---

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Extend #17 Home with an actionable `/Coordinator/Work` queue combining requests, uncovered/unconfirmed commitments, current failures, recurring withdrawals/gaps/zone reviews/handoffs, ordered by severity then local start. | Must | Configurable SoonHours default 72 and UrgentHours default 24. |
| FR2 | Every queue row must expose one narrow existing intervention and plain context; filters/counts derive from the same authoritative projection/snapshot rules. | Must | No bulk mutation or vanity chart. |
| FR3 | Replace full known-volunteer lists with antiforgery-protected search requiring 3–100 characters and returning at most 10 prefix matches by normalized name/email, with ID/name/email only. | Must | Exclude #16 anonymized records; never put search contact in query strings/logs. |
| FR4 | Add structured nullable ShiftId/VolunteerId correlation to immutable audit entries, backfill existing JSON where safely parseable, and index actor/action/date/shift/volunteer. | Must | DetailJson remains authoritative but is never routine output. |
| FR5 | Add group-local date, actor, shift, volunteer-reference, and action-category filters plus 50-event descending keyset pagination using opaque protected cursors. | Must | No total-count query or offset paging. |
| FR6 | Expand human event summaries for all approved action/notification/recurrence/privacy/access categories and provide a generic nontechnical historical fallback. | Must | No raw internal codes/IDs/JSON. |
| FR7 | Add `/Coordinator/Access` diagnostics for OIDC configuration completeness, distinct normalized allowlist, current user, and latest `CoordinatorAccessVerified` audit; explicit verify POST records actor without changing access. | Must | Runtime/Railway configuration remains authoritative. |
| FR8 | Define and enforce fixed SQL-command budgets under representative data for coordinator projections; replace remaining request/coverage/audit/roster per-row loading with set-based queries. | Must | PostgreSQL only; no cache hiding N+1. |
| FR9 | Add PostgreSQL, authorization, privacy, pagination, performance, browser, keyboard, mobile, and handoff coverage with exact no-leak/no-training behaviors. | Must | Deterministic data/clock/zone. |

---

## Out of Scope

- Analytics dashboards, trend charts, exports, custom reports, or a data warehouse
- A general membership/volunteer directory or broad contact browsing
- In-app OIDC provider, secret, or Railway allowlist administration
- New bulk workflow mutations
- Changes to volunteer privacy retention, scheduling policy, recurrence, hub, or delivery state machines

---

## Versioning

The `enhancement` label requires one minor version increment from the implementation branch's current root `VERSION`.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #22 | 2026-09-03 | Initial feature spec |
