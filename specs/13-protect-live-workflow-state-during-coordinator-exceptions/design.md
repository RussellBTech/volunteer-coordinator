# Design: Protect Live Workflow State During Coordinator Exceptions

**Issue**: #13
**Date**: 2026-09-02
**Status**: Approved
**Author**: RussellBTech

---

## Overview

Extend the existing Domain → Application → Infrastructure → Web flow without adding another service or persistence model. `VolunteerCoordinatorService` continues to own transaction orchestration through `IWorkflowStore`; existing domain transitions (`Shift.Deactivate`, `ShiftRequest.Supersede`, `Assignment.Cancel`, and `ActionToken.Invalidate`) remain the only state mutators. EF Core and PostgreSQL provide atomic commit, per-slot serialization, optimistic concurrency, and the existing partial uniqueness constraints.

Shift deactivation is a cascading administrative resolution. It locks every shift slot in ascending identifier order, reads live requests and assignments only after acquiring those locks, resolves them and their unused assignment tokens, records per-assignment and shift audit entries, and commits once. Direct coordinator cancellation follows the same assignment/token/audit semantics for one assignment and slot. Notification delivery is not added by this issue.

---

## Architecture and Data Flow

1. An authorized Razor Page POST supplies the coordinator email, entity identifier, and, for deactivation, expected shift version.
2. `VolunteerCoordinatorService` starts `IWorkflowStore.ExecuteInTransactionAsync` and normalizes the coordinator through the existing `RequireCoordinator` path.
3. Every command that creates, ends, acts on, or generates links for an assignment serializes through `LockSlotAsync` before its authoritative active-state read. Multi-slot deactivation acquires locks in ascending `Guid` order to avoid lock-order inversions; assignment-id/token preflight reads may locate the immutable slot identifier, but the active assignment and token are revalidated after the lock.
4. Existing domain methods perform each transition. The application invalidates every unused token attached to each administratively ended assignment and appends audits in the same unit of work.
5. `EfWorkflowStore` saves once and commits. Existing `DbUpdateConcurrencyException` and PostgreSQL unique-violation translation returns the current reload-and-retry domain error while rollback prevents partial resolution.
6. Coordinator request and coverage GETs project authoritative assignment state from PostgreSQL; pages do not infer state locally.

---

## Application and Port Changes

### `VolunteerCoordinatorService`

- Keep `DeactivateShiftAsync(Guid shiftId, uint expectedVersion, string coordinatorEmail, CancellationToken cancellationToken)` as the public signature, but replace its shift-only mutation with the full atomic resolution flow.
- Add `Task CancelAssignmentAsync(Guid assignmentId, string coordinatorEmail, CancellationToken cancellationToken)`. It obtains the immutable slot ID through `GetAssignmentSlotIdAsync`, locks that slot, then loads the assignment authoritatively; a missing ID raises `DomainException("The assignment was not found.")`, and a non-active assignment raises the existing `DomainException("Only an active assignment can be cancelled.")` without mutation.
- Add a private helper that accepts the loaded active assignment set, current UTC instant, coordinator email, and cancellation token; for each assignment it calls `Assignment.Cancel`, invalidates all returned unused tokens, and adds `AssignmentCancelledByCoordinator` for direct cancellation or `AssignmentCancelledByShiftDeactivation` for cascading deactivation. Audit details include `ShiftSlotId`, `VolunteerId`, and the terminal status, never raw tokens.
- Update `SupersedeConflictingAssignmentsAsync` so immediately after `Assignment.Reassign(now)` it invalidates every unused action token for that ended assignment in the same transaction, before the existing `AssignmentReassigned` audit and flush. This closes the existing coordinator-reassignment token gap without changing the reassignment API or terminal status.
- Update `GenerateActionLinksAsync` to obtain the immutable slot identifier through `GetAssignmentSlotIdAsync`, lock that slot, then load and validate the assignment before invalidating/generating tokens. Update `ApplyActionAsync` to hash the raw token, obtain its assignment slot through `GetActionTokenSlotIdAsync`, lock that slot, then call `ResolveActionAsync` so token and assignment entities are first tracked from authoritative post-lock state. Unknown/invalid preflight tokens retain `DomainException("This action link is invalid, expired, or already used.")`. A deactivation that commits first therefore prevents new links/actions; a link/action that commits first is observed and resolved by deactivation.
- After deactivation resolution, retain the existing `ShiftDeactivated` audit action but replace `{}` detail with the counts of superseded requests, cancelled assignments, and invalidated tokens.
- In `ListRequestsAsync`, collect distinct request slot identifiers, load active assignments once, and compute slot state with this precedence: `Inactive`, `Ended`, `Unconfirmed` for `AssignmentStatus.Assigned`, `Confirmed` for `AssignmentStatus.Confirmed`, otherwise `Available`. `CanApprove` remains true only for a pending request whose slot state is `Available`.

### `IWorkflowStore` and `EfWorkflowStore`

Add these reads alongside the existing single-slot and per-action methods:

