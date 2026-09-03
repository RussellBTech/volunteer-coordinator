# Requirements: Deliver Transactional Email With Observable Retry and Resend

**Issue**: #19
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As a** volunteer and coordinator
**I want** private scheduling links delivered reliably with visible failure handling
**So that** the workflow does not depend on copying raw URLs by hand

---

## Background

The Application notification port currently resolves to an unavailable adapter. Workflow transitions correctly commit first, but every notification attempt fails immediately, no production template/provider exists, and coordinators cannot distinguish pending, accepted, delivered, bounced, or exhausted delivery.

The approved provider is Resend through its official .NET SDK and HTTP idempotency-key contract. PostgreSQL stores non-secret notification intent and bounded attempt state; an in-process worker sends after workflow commit. Transient delivery gets five total attempts at 0, 1, 5, 30, and 120 minutes. Signed, deduplicated Resend webhooks advance accepted messages to delivered, bounced, or complained. Issue #18 supplies commitment hub/recovery link material, and issue #15 supplies complete group-local context.

---

## Acceptance Criteria

### AC1: Automatic volunteer delivery

**Given** a request, assignment, access reissue/recovery, cancellation, or volunteer-visible schedule correction commits successfully
**When** notification processing runs
**Then** Resend receives one safe plain-text/HTML message intent for the current volunteer contact, with group-local commitment context and any required private access material generated transiently

### AC2: Authoritative workflow independent from delivery

**Given** Resend times out, rejects, bounces, or reports a complaint
**When** scheduling and notification transactions complete
**Then** workflow/audit state remains successful, while notification state independently records pending, accepted, delivered, bounced/complained, retrying, or final failure without reverting the scheduling transition

### AC3: Bounded idempotent retry

**Given** a delivery receives a transient network, timeout, rate-limit, concurrency-idempotency, or server failure
**When** the in-process worker retries
**Then** it uses PostgreSQL leasing, the same Resend idempotency key for an identical in-memory provider attempt, fresh safe material after abandoned attempts, and no more than five total scheduled attempts at 0, 1, 5, 30, and 120 minutes

### AC4: Coordinator resend and observability

**Given** a message is pending, retrying, accepted, delivered, bounced, complained, or failed, or a volunteer asks for another copy
**When** an authenticated coordinator views Messages and requests resend/reissue
**Then** plain-language current state and attempt history are visible, the new intent is deduplicated and audited, and access invalidation follows issue #18's normal versus revoke-now policy

### AC5: Secret and content safety

**Given** Resend credentials, webhook events, templates, and private capabilities are processed
**When** committed configuration, PostgreSQL, logs, audit details, and HTTP output are inspected
**Then** provider/webhook secrets and raw capability/recovery URLs are absent, webhook signatures are verified over the raw body, recipient/contact values are not duplicated in notification persistence, and untrusted template fields are safely encoded

---

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Implement a Resend adapter behind the Application delivery port using the official .NET SDK, configured API key, verified sender, reply-to, trusted public base URL, and provider idempotency keys. | Must | Secrets only from Railway/environment variables. |
| FR2 | Replace fire-and-forget post-command calls with a PostgreSQL notification intent written in the same transaction as each successful workflow transition; provider I/O always occurs afterward. | Must | Intent persistence may fail the workflow transaction; provider outcome may not. |
| FR3 | Add encoded plain-text and HTML templates for request receipt, assignment/hub access, recovery/reissue, request decision, volunteer-visible correction, coordinator/volunteer cancellation, and deactivation. | Must | #15 local context; #18 transient access material; no coordinator-only notes. |
| FR4 | Run an in-process bounded worker using ordered batches, `FOR UPDATE SKIP LOCKED`, leases, cancellation, five total attempts at 0/1/5/30/120 minutes, and explicit transient/permanent classification. | Must | No separate worker process. |
| FR5 | Use Resend's 24-hour idempotency key per materialized provider attempt. Reuse it only while the exact payload remains in memory; abandoned secret-bearing attempts generate fresh short-lived material and a new attempt/key. | Must | At-least-once delivery is explicit; never duplicate workflow transitions. |
| FR6 | Add a signed `/webhooks/resend` endpoint for delivered, bounced, and complained events, verify raw body and Svix headers/timestamp before parsing, deduplicate event IDs, and handle out-of-order events monotonically. | Must | No recipient or raw webhook payload persistence/logging. |
| FR7 | Add coordinator Messages projections and antiforgery-protected resend/reissue controls with plain states, attempt timing, safe failure categories, affected commitment, and audits. | Must | #18 governs access replacement/revocation. |
| FR8 | Store recipient by volunteer identifier and resolve current email only when sending; store provider message ID, safe category, timing, and counters but no rendered body, destination copy, credentials, or raw link. | Must | #16 anonymization cancels pending intent before contact removal. |
| FR9 | Add contract, PostgreSQL, webhook, Web, and provider-sandbox coverage for template safety, scheduling, retries, leases, idempotency, crash recovery, state ordering, secret absence, workflow independence, and coordinator resend. | Must | No production recipient in tests. |

---

## Out of Scope

- Marketing/bulk campaigns, mailing lists, analytics pixels, or open/click tracking
- SMS, push, or voice delivery
- A separate worker process or distributed queue service
- Guaranteeing inbox placement after the recipient mail server reports delivery
- Volunteer-wide accounts or access behavior beyond issue #18

---

## Versioning

The `enhancement` label requires one minor version increment from the implementation branch's current root `VERSION`.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #19 | 2026-09-03 | Initial feature spec |
