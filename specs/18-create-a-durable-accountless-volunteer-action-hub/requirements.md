# Requirements: Create a Durable Accountless Volunteer Action Hub

**Issue**: #18
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As a** volunteer
**I want** one private link that remains useful through my commitment
**So that** I can see status and take currently valid actions without an account or repeated coordinator intervention

---

## Background

The request-status link is read-only and uses a fixed 30-day lifetime. Confirm, decline, and cancel use separate seven-day links that a coordinator generates and transmits manually. Fixed expiry can precede a distant shift, direct assignments have no durable status capability, and a lost link has no non-enumerating recovery flow.

The approved hub is one reusable bearer capability per volunteer and shift-slot commitment. It spans request through terminal assignment status, remains readable until seven days after the authoritative shift end, and exposes only actions valid for the current assignment state and time. Anonymous recovery requires email plus the group-local commitment date and sends a 30-minute single-use recovery token. Existing hub links are invalidated only when that token is redeemed, preventing recovery-request denial of service.

Implementation requires issue #15's complete local commitment projection, issue #14's anonymous abuse limits, and issue #19's real transactional-email delivery. Existing separate action links receive an explicit compatibility exception: no new link is generated after cutover, but an already-issued link remains usable only through its stored maximum seven-day expiry.

---

## Acceptance Criteria

### AC1: Unified commitment capability

**Given** a volunteer has one valid private capability for a request or direct assignment
**When** request/assignment state changes
**Then** the same hub displays complete group-local commitment context and current status, offering Confirm and Decline only before shift start for an assigned volunteer and Cancel only before shift end for a confirmed volunteer

### AC2: Commitment-bound access

**Given** a commitment is more than 30 days away
**When** the volunteer returns any time before seven days after the current authoritative shift end
**Then** the hashed-at-rest capability remains usable for read-only status, follows approved shift date corrections, and expires without extending mutation deadlines

### AC3: Non-enumerating recovery

**Given** a volunteer submits email plus group-local commitment date to recover a lost link
**When** the values match or do not match eligible commitments
**Then** the caller always receives the same generic response, while exact matches enqueue email delivery of 30-minute single-use recovery tokens and no current hub is invalidated until a token is redeemed

### AC4: Atomic action and replacement

**Given** concurrent requests try to act through, redeem recovery for, revoke, or replace the same commitment capability
**When** PostgreSQL commits them under the slot lock and concurrency constraints
**Then** one serially valid transition wins, the winning recovery redemption atomically consumes its token, invalidates prior hubs, and issues one new raw capability, and replay changes nothing

### AC5: Coordinator access visibility and reissue

**Given** automatic delivery or recovery fails, or a hub is suspected compromised
**When** an authenticated coordinator views coverage
**Then** plain-language access/delivery state is visible and the coordinator can either send replacement access while preserving current access until redemption or explicitly revoke now and send replacement, with consequences, actor, and outcome audited

---

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Add one `VolunteerAccessCapability` per volunteer/slot commitment with random token hash, creation and invalidation metadata, and active uniqueness. Evolve the current request-status token into this capability so the same distributed link remains useful after assignment. | Must | Raw hub tokens remain transient and never persist/log. |
| FR2 | Resolve hub validity from active capability state plus the current shift end and a fixed seven-day read grace. Offer Confirm/Decline only while Assigned and before start; offer Cancel only while Confirmed and before end. | Must | Terminal/late hubs remain read-only until grace expiry. |
| FR3 | Apply hub actions in one transaction after hashing the raw capability, locating/locking the slot, and reloading capability, request, assignment, and shift. | Must | Replays and competing actions make no second mutation. |
| FR4 | Add an accountless recovery form requiring email and group-local commitment date with one generic response, per-IP limits from #14, and no synchronous existence disclosure. | Must | Matching several commitments on one date may enqueue one separately scoped recovery intent per commitment. |
| FR5 | Deliver 30-minute, single-use recovery tokens through #19 without persisting raw values. Redemption issues the new hub raw token to the browser and invalidates every older active capability for that volunteer/slot atomically. | Must | Unrequested/expired/replayed tokens disclose no match. |
| FR6 | Create and deliver a hub capability automatically for new direct assignments and retain the existing capability when a request is approved/reassigned for the same volunteer/slot. | Must | Delivery failure does not roll back workflow state and is coordinator-visible. |
| FR7 | Replace coordinator-generated action links with access state plus audited normal reissue and `revoke now` reissue. Normal reissue preserves old hubs until redemption; revoke-now invalidates immediately before queuing delivery. | Must | Consequence preview required for immediate revocation. |
| FR8 | Stop generating legacy action tokens at cutover. Continue accepting only previously issued, unexpired action tokens through their stored expiry, after which replay is uniformly invalid. | Must | Explicit approved transitional exception; no lifetime extension or compatibility alias for new work. |
| FR9 | Integrate shift deactivation/reassignment from #13 and privacy anonymization from #16 so administratively ended/removed access and recovery tokens become unusable in the same authoritative transaction. | Must | Request rejection/supersession remains readable but non-actionable through grace unless administratively revoked. |
| FR10 | Add PostgreSQL and Web coverage for long-future validity, lifecycle deadlines, non-enumeration, raw-token absence, recovery delivery/redeem, coordinator reissue/revoke, migration of status capabilities, legacy expiry, and conflicting transitions. | Must | Use separate contexts and explicit slot locks for concurrency. |

---

## Out of Scope

- Persistent volunteer accounts, passwords, passkeys, or volunteer-wide identity sessions
- A single link spanning multiple commitments or recurring-series enrollment
- SMS or voice recovery
- Extending Confirm/Decline beyond shift start or Cancel beyond shift end
- Notification provider implementation outside issue #19

---

## Versioning

The `enhancement` label requires one minor version increment from the implementation branch's current root `VERSION`.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #18 | 2026-09-03 | Initial feature spec |
