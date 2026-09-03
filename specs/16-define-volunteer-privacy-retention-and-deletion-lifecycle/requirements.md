# Requirements: Define Volunteer Privacy Retention and Deletion Lifecycle

**Issue**: #16
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As an** accountless volunteer
**I want** to understand and control the contact data used for scheduling
**So that** participation does not create indefinite or misleading personal records

---

## Background

The accountless request flow requires a name and email and optionally accepts a phone number, but it gives no purpose, visibility, retention, or removal explanation. The same contact record is reused by coordinator assignment and notification attempts, and relational workflow history currently prevents simple deletion. There is no approved retention job or coordinator-assisted removal path.

The approved policy keeps identifiable volunteer contact data only while a live request or assignment needs it and for at most one year after the latest related workflow activity or shift end. Eligible records are irreversibly anonymized in place so non-identifying schedule, request, assignment, notification outcome, and audit integrity can remain indefinitely. A verified coordinator may process an earlier request after matching normalized email and one recent commitment supplied out of band. The application records service scheduling only and must never characterize a volunteer as an AA member or attendee.

---

## Acceptance Criteria

### AC1: Informed collection

**Given** a volunteer or coordinator opens any form that collects volunteer contact data
**When** name, email, or phone fields are presented
**Then** concise text before submission explains scheduling purpose, required and optional fields, coordinator visibility, one-year retention, protected-backup rotation, and the configured removal contact without claiming membership or attendance

### AC2: Data minimization

**Given** phone is optional and no value is supplied
**When** a request or assignment is created
**Then** the workflow succeeds, `Phone` remains null, and no placeholder, inferred, or duplicate personal value is stored in its place

### AC3: Deterministic retention execution

**Given** a volunteer has no pending request or active assignment and the greatest related activity/shift timestamp is more than 365 days old
**When** the retention process locks and revalidates that volunteer
**Then** name, email, normalized email, phone, notification destinations, and usable private links are irreversibly removed or invalidated atomically, one non-identifying audit outcome is recorded, and relational workflow history remains valid

### AC4: Coordinator-assisted removal

**Given** a volunteer requests removal out of band and identifies their normalized email plus one recent related commitment
**When** an authorized coordinator verifies that match and confirms removal
**Then** an eligible volunteer is anonymized immediately, an ineligible live dependency is explained without partial mutation, and the audit records actor, reason, volunteer identifier, and counts without copying removed values

### AC5: No membership claim or residual application identity

**Given** a volunteer has been anonymized or any application page describes volunteer data
**When** coordinator views, public status, notification history, logs, and audit details are inspected
**Then** no original volunteer contact value or membership/attendance characterization remains in application-managed current data, and anonymized records display a neutral `Removed volunteer` identity

---

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Add the approved privacy classification and lifecycle to `steering/product.md` and `steering/tech.md`: contact identity is confidential, retained for 365 days after the deterministic anchor when no live dependency exists, while anonymized workflow/audit history is retained indefinitely. | Must | Coordinator identity/audit retention is a separate policy and is unchanged. |
| FR2 | Add one reusable plain-language privacy notice to every volunteer-contact collection form and a public `/Privacy` page. Require a configured removal-contact address. | Must | Phone stays optional; text states service scheduling only, not AA membership or attendance. |
| FR3 | Add `Volunteer.AnonymizedAtUtc` and an irreversible anonymization transition that replaces name/email with neutral non-personal tombstones derived only for uniqueness, clears phone, and prevents later contact updates on that record. | Must | Do not hash or encrypt the original values as a substitute for deletion. |
| FR4 | In the same transaction, invalidate every request-status and unused assignment-action token, redact related notification destinations, and append one `VolunteerAnonymized` audit containing no removed value. | Must | Existing request/assignment/status history and surrogate IDs remain. |
| FR5 | Define the automatic eligibility anchor as the greatest of volunteer update, related request activity, assignment activity, notification activity, and related shift end. Require no pending request and no assigned/confirmed assignment at execution time. | Must | Exactly 365 elapsed UTC days; future shifts keep data ineligible even if other activity is old. |
| FR6 | Run a bounded in-process retention sweep on startup and every 24 hours, using PostgreSQL volunteer-row locks and post-lock eligibility checks. Coordinator removal uses the same application command with reason `CoordinatorRequest`. | Must | Idempotent across concurrent Web processes; one audit per volunteer. |
| FR7 | Serialize request/direct-assignment contact reuse and anonymization through the same volunteer-row lock so a record cannot become live or regain contact data while removal commits. | Must | A later request with the removed email creates a new volunteer record. |
| FR8 | Add an authenticated `/Coordinator/Privacy` lookup and confirmation flow that requires normalized email plus selection of one recent related commitment before it exposes eligibility and removal action. | Must | Antiforgery, allowlist, generic not-found text, explicit dependency reason. |
| FR9 | Add deterministic unit, PostgreSQL, and Web coverage for retention boundaries, live-dependency blocking, concurrency, token invalidation, notification redaction, audit privacy, optional phone, notices, coordinator verification, and membership-language regression. | Must | Tests use fixed UTC instants and isolated PostgreSQL. |

---

## Out of Scope

- Legal advice, jurisdiction-specific compliance certification, or deletion of provider infrastructure logs
- Persistent volunteer accounts or volunteer self-service identity verification
- Removal of coordinator identities from immutable coordinator audit history
- Deletion of non-identifying schedule, request, assignment, notification-outcome, or audit records
- Immediate erasure from immutable backups; protected copies expire through the documented backup rotation and retention reruns after restore

---

## Versioning

The `enhancement` label requires one minor version increment from the implementation branch's current root `VERSION`.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #16 | 2026-09-03 | Initial feature spec |
