# Design: Support Policy-Based Self-Scheduling and Recurring Service Commitments

**Issue**: #21
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Overview

Extend existing concrete request/assignment workflows rather than replacing them. A persisted policy chooses whether public submission creates a pending request or wins an immediate confirmed assignment. Recurring participation is a separate bounded aggregate above issue #20 occurrences; every realized commitment still becomes an ordinary concrete `Assignment`, so coverage and exception state remain authoritative per occurrence.

All public writes use issue #14 limits, issue #16 privacy notice/lifecycle, issue #18 accountless capabilities, and issue #19 notification intent. Issue #20 occurrence identity/exceptions and issue #17 consequence previews/navigation are prerequisites.

---

## Scheduling Policy

Add Domain enum `SignupPolicy { ApprovalRequired, DirectClaim }`. Add required policy to `Shift` and `RecurringShiftSeriesRevision`. One policy applies to every active primary/backup slot in that shift/series revision. The migration writes ApprovalRequired for every existing row and does not silently enable self-scheduling.

New standalone/series forms preselect Approval required. The choice cards say:

- **Approval required (safer default):** `A volunteer sends a request. A coordinator reviews it before the commitment is assigned.`
- **Direct claim (lower maintenance):** `The first eligible volunteer who submits is confirmed immediately. A coordinator can still correct coverage later.`

Changing a published standalone shift from approval to direct claim is blocked while pending requests exist; the preview links to resolve them first. Changing direct claim to approval affects only future submissions and never reopens an active assignment. Series policy changes create an effective #20 revision and affect only submissions targeting that revision; existing recurring requests/commitments retain `SourcePolicy`. Every change uses expected version, explicit confirmation, normalized coordinator audit, and no hidden consequence.

Public pages display one banner immediately before contact/cadence fields: `Coordinator review required` or `Direct claim — submitting confirms this commitment immediately.` The final button matches: `Send request` or `Claim commitment`.

---

## One-Time Direct Claim

`ClaimShiftAsync` accepts slot ID, contact input, coordinator-independent request context, and cancellation. In one transaction:

1. load shift/slot policy metadata, lock the slot, then reload active/published/future state and policy;
2. locate and lock an existing volunteer through issue #16's contact serialization, or create one;
3. reject if an active assignment now exists or the volunteer already has an active assignment for the shift;
4. handle any stale pending request by the same volunteer as approved/superseded according to existing request invariants, but never jump unresolved requests from other volunteers when the coordinator has not first changed policy (policy change was blocked while pending exist);
5. create `Assignment` directly in Confirmed state through a new explicit `Assignment.DirectClaim` factory/transition, recording assigned and confirmed UTC together;
6. create #18 commitment capability, #19 assignment-access intent, and `AssignmentDirectClaimed` audit;
7. commit once and return hub receipt.

The slot lock plus existing active-assignment unique indexes creates first-writer-wins. The loser returns `This commitment was just claimed. Choose another opening.` and does not persist a new volunteer, request, capability, intent, or audit. Do not catch/translate a unique conflict outside the transaction without rollback.

ApprovalRequired continues the existing request flow.

---

## Recurring Participation Model

### `RecurringCommitmentRequest`

Fields: ID, series/revision, volunteer, selected `SlotKind`/Position, effective/end `DateOnly`, requested UTC, status (`Pending`, `Approved`, `Rejected`, `Superseded`), nullable resolved UTC/coordinator, recurring access capability identity, and concurrency token. Unique active request prevents duplicate pending requests by volunteer/series/role/range.

### `RecurringCommitment`

Fields: ID, series, volunteer, role, effective/end local dates, `SourcePolicy`, nullable source request, state (`AwaitingConfirmation`, `Active`, `Withdrawn`, `Completed`), created/confirmed/withdrawn UTC, nullable withdrawal effective local date, and concurrency version. Store one finite inclusive range of 4–26 weeks, default 12.

PostgreSQL prevents overlapping AwaitingConfirmation/Active date ranges for the same series role using an exclusion constraint on series ID, normalized role key, and inclusive `daterange`. Enable only the required PostgreSQL extension/mapping. Application also locks the series/role before overlap reads for clear errors.

### `RecurringCommitmentOccurrence`

One row per series occurrence considered by a commitment: commitment ID, recurring occurrence ID, state (`Assigned`, `Confirmed`, `SkippedException`, `Withdrawn`, `Replaced`), nullable concrete assignment ID, and reason. Unique `(CommitmentId, RecurringOccurrenceId)` makes reconciliation idempotent; unique active relation to occurrence/role plus concrete assignment constraints prevents double materialization.

SkippedException records visible reasons such as already filled, manual exception, Needs review, Skipped, unpublished, or inactive. It never overwrites an occurrence. At least one eligible concrete published occurrence must be assigned before direct claim/approval succeeds.

### Capability

Add one recurring capability/hash scoped to `RecurringCommitmentRequest` then `RecurringCommitment`, separate from issue #18's per-occurrence hub. The same raw link shows request/approval/cadence status, confirms the approved cadence once, lists included/skipped concrete local commitments, and permits future withdrawal. Hash-at-rest, no-store/no-referrer, generic invalid/recovery, seven-day post-final-end read grace, and coordinator reissue rules mirror #18.

---

## Recurring Submission and Approval

The public series detail offers only generated published occurrences. Volunteer chooses role, effective occurrence, and horizon 4–26 weeks. Server preview enumerates exact dates in range and labels each Included or unavailable exception. It shows policy and states that future #20 corrections require coordinator handoff. Final submission re-enumerates and locks eligible slots in ascending ID order.

### DirectClaim

Under DirectClaim, create one Active recurring commitment and Confirmed assignments for every eligible included occurrence in one transaction. Insert skipped rows for exceptions, capability, intent, and audit. If another overlapping commitment wins, the whole transaction rolls back with clear no-longer-available text. A commitment with zero included occurrences is rejected.

### ApprovalRequired

Create one pending recurring request and capability; do not reserve slots. Coordinator Requests preview shows exact currently eligible/skipped dates. Approval locks series/role and all eligible slots, rechecks no overlapping commitment, creates one AwaitingConfirmation commitment with Assigned concrete assignments, approves the selected request, supersedes conflicting pending requests for the same role/range, and sends one cadence-confirmation intent. Rejection changes only request state.

The recurring hub offers `Confirm this recurring commitment` once. Final POST locks all still-Assigned cadence slots deterministically, revalidates range/association, confirms them and commitment atomically, leaves occurrence exceptions/replaced assignments untouched, and sends one summary receipt. If no eligible assignment remains, confirmation fails without activating commitment. Replay shows Active and mutates nothing.

No weekly confirmation is required. Individual occurrence hubs continue to show/cancel their concrete assignment.

---

## Materialization and Reconciliation

Issue #20 top-up notifies a recurrence-participation reconciler after new occurrences commit, or the daily hosted service processes both stages in order. For each AwaitingConfirmation/Active commitment whose finite range includes a new occurrence:

- lock commitment then slot in stable order;
- if generated, published, matching role, non-exception, and open, create Assignment with status matching commitment (`Assigned` or `Confirmed`) plus occurrence hub/intent;
- otherwise create/update a visible SkippedException row;
- never override an assignment or clear occurrence exception;
- remain idempotent under unique join/assignment constraints.

Because every commitment end lies within the requested/generated 4–26 week range, creation ensures all currently expected dates already have occurrence rows. Top-up mainly handles #20 revisions/reconciliations, not unbounded extension.

Completion occurs after the final occurrence ends and leaves history/capability readable through grace. Renewal is a new bounded commitment with a non-overlapping effective range.

---

## Withdrawal and One-Occurrence Exceptions

Recurring hub lists eligible future occurrence boundaries. `Withdraw from this date` preview shows every included future assignment, local date/role, cancellation/link consequence, reopened coverage, and notifications. Effective date must be an included occurrence whose start is in the future.

Final transaction locks commitment and affected slots ascending, reloads mappings, calls existing `Assignment.Cancel` for Assigned/Confirmed cadence assignments at/after the boundary, invalidates individual capabilities/action tokens, marks join rows Withdrawn, sets commitment state/range withdrawal, writes #19 intents and one `RecurringCommitmentWithdrawn` audit, and commits all-or-none. Earlier assignments/history and SkippedException/Replaced rows remain. Replay does nothing.

Cancelling one concrete assignment through issue #18 marks its join row/issue-#20 occurrence exception (`Withdrawn` for volunteer cancellation or `Replaced` for coordinator substitution) but leaves recurring commitment Active and later rows unchanged. Reconciler never refills that occurrence from the cadence.

Coverage/home show reopened dates normally and label the series-level coverage risk for coordinator attention.

---

## Series Revision Handoff

Issue #20 default correction leaves protected recurring assignments on old occurrences as detached exceptions. This issue adds explicit `Move recurring commitments to revised schedule` per affected recurring commitment.

Preview maps old future join rows to eligible new-revision occurrences in the same effective/end range and role. It lists old assignments to cancel, new occurrences to assign, exceptions/conflicts to skip, local time changes, link invalidation/reissue, and #19 messages. No mapping is inferred by ordinal alone; use series identity, role, effective boundary, and concrete revision occurrence dates.

Final handoff locks commitment, old/new occurrences and slots in deterministic order, revalidates the exact mapping, cancels old assignments, marks old joins Replaced, creates new joins/assignments with state matching commitment, capabilities/intents, and audit in one transaction. Any stale/conflicting included target rejects all changes. Exceptions remain. Notification failure after commit cannot roll back handoff.

No automatic handoff occurs on series revision or group-zone adoption.

---

## Web Experience

Standalone openings branch their call to action by visible policy. Recurring series pages offer `Serve once` on an occurrence and `Serve regularly` on the series. The cadence flow uses #17 server-rendered steps:

1. choose role;
2. choose first occurrence and 4–26 week horizon (12 preselected);
3. review exact Included/Skipped dates and policy outcome;
4. submit request or claim;
5. show one private recurring hub/access delivery outcome.

Coordinator series/shift forms show the two policy cards and consequence preview. Requests supports recurring approvals. Coverage groups concrete assignments but labels cadence and exceptions. Coordinator recurring detail exposes withdrawal risk, substitute, and revision handoff without raw IDs/technical recurrence terms.

At 320px, date lists use cards/ordered lists. Keyboard and assistive technology receive policy, role, range, included/skipped state, confirmation, and consequence text. No drag/drop, color-only state, or account terminology.

---

## Security, Privacy, and Concurrency

- Public mutations use #14 rate limits, #16 notices/contact locks, antiforgery, generic stale/no-longer-available results, and no identity enumeration.
- Capabilities are random/hash-at-rest and scoped separately to one finite recurring commitment or one occurrence.
- Direct claim and recurring materialization lock slots before active reads; exclusion/unique constraints remain final enforcement.
- Multi-slot confirmation/withdrawal/handoff locks ascending IDs, rechecks authoritative state, and commits once.
- #19 intent is transactionally recorded but provider outcome remains independent.
- Audits contain actor/capability/series/occurrence/assignment identifiers and counts, never raw tokens/contact/free text.

---

## Performance Considerations

Commitments are capped at 26 weeks and operate only on #20's bounded concrete occurrences. Batch-load eligibility and lock only selected slots. Exclusion/range, active commitment, join, capability hash, and occurrence-role indexes support lookup.

Daily reconciliation batches commitments and occurrences and is idempotent. Public preview caps rendered occurrence rows to the selected finite range; no unbounded recurrence expansion.

---

## Testing Strategy

| Layer | Type | Coverage |
|-------|------|----------|
| Domain/Application | Unit | Policy text/state, direct-confirm factory, range/horizon, commitment confirmation/withdrawal, occurrence mapping. |
| PostgreSQL | Integration | Policy migration, direct-claim races, exclusion overlap, deterministic multi-slot locks, approval races, materializer idempotency, withdrawal/handoff rollback. |
| Web | Integration/browser | Visible policy, exact date preview, request/direct outcomes, one confirmation, recurring/occurrence hubs, substitute, mobile/keyboard. |
| Cross-feature | Integration | #14 limiting, #16 anonymization, #18 access, #19 delivery, #20 revision exceptions/top-up, #17 attention/previews. |

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #21 | 2026-09-03 | Initial feature spec |
