# Tasks: Protect Live Workflow State During Coordinator Exceptions

**Issue**: #13
**Date**: 2026-09-02
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Workflow | 2 | [ ] |
| Presentation | 1 | [ ] |
| Verification | 2 | [ ] |
| **Total** | 5 | |

---

## Phase 1: Workflow

### T001: Resolve deactivation and coordinator cancellation atomically

**File(s)**: `src/VolunteerCoordinator.Application/VolunteerCoordinatorService.cs`; `src/VolunteerCoordinator.Application/Ports/IWorkflowStore.cs`; `src/VolunteerCoordinator.Infrastructure/Persistence/EfWorkflowStore.cs`
**Type**: Modify
**Depends**: None
**Acceptance**:
- [ ] `DeactivateShiftAsync` locks all shift slots in deterministic order, preserves expected-version validation, and commits shift deactivation, pending-request supersession, active-assignment cancellation, unused-token invalidation, and coordinator audits as one transaction
- [ ] `CancelAssignmentAsync` accepts assigned or confirmed assignments, locks the slot, cancels the assignment, invalidates all unused assignment tokens, and writes `AssignmentCancelledByCoordinator` atomically
- [ ] existing coordinator reassignment invalidates every unused token for the superseded assignment before the replacement assignment commits
- [ ] action-link generation and volunteer action application serialize on the assignment's slot and revalidate current assignment/token state after the lock, preventing a usable token from being created or applied after deactivation
- [ ] inactive/stale assignments and transaction conflicts fail without partial mutation
- [ ] batch pending-request and unused-token store reads return tracked rows and short-circuit empty identifier collections
- [ ] no migration, new status, notification behavior, or raw-token persistence is introduced

### T002: Project assignment-aware request state

**File(s)**: `src/VolunteerCoordinator.Application/VolunteerCoordinatorService.cs`
**Type**: Modify
**Depends**: T001
**Acceptance**:
- [ ] request projection loads active assignments once for all represented slots
- [ ] assigned slots report `Unconfirmed`, confirmed slots report `Confirmed`, and neither reports `Available`
- [ ] `CanApprove` is false for every request whose slot has an active assignment
- [ ] inactive and ended precedence remains unchanged

---

## Phase 2: Presentation

### T003: Expose coordinator cancellation on coverage

**File(s)**: `src/VolunteerCoordinator.Web/Pages/Coordinator/Coverage/Index.cshtml.cs`; `src/VolunteerCoordinator.Web/Pages/Coordinator/Coverage/Index.cshtml`; `src/VolunteerCoordinator.Web/Pages/Coordinator/Schedule/Index.cshtml.cs`; `src/VolunteerCoordinator.Web/Pages/Coordinator/Schedule/Index.cshtml`
**Type**: Modify
**Depends**: T001, T002
**Acceptance**:
- [ ] an authorized antiforgery-protected coverage POST cancels the selected active assignment through `VolunteerCoordinatorService`
- [ ] success redirects to coverage with `Assignment cancelled. The slot is now uncovered.` and the row becomes `Uncovered`
- [ ] domain failures redirect with textual error feedback and no partial mutation
- [ ] schedule deactivation explains its cascading consequences before submit, labels the action `Deactivate and resolve`, and confirms that requests, assignments, and action links were resolved

---

## Phase 3: Verification

### T004: Add PostgreSQL lifecycle and concurrency coverage

**File(s)**: `tests/VolunteerCoordinator.IntegrationTests/WorkflowIntegrationTests.cs`
**Type**: Modify
**Depends**: T001, T002
**Acceptance**:
- [ ] deactivating a shift containing pending requests plus assigned and confirmed assignments leaves the shift inactive, requests superseded, assignments cancelled, all unused action tokens unusable, and coordinator audits committed
- [ ] direct coordinator cancellation reopens the slot and rejects every previously issued action link without mutation
- [ ] coordinator reassignment rejects every previously issued link for the superseded assignment without changing the replacement assignment
- [ ] request and coverage queries return matching `Unconfirmed` and `Confirmed` states for active assignments
- [ ] separate-context PostgreSQL tests use an explicitly held slot-row lock to prove deactivation waits for in-flight assignment/request/link/action work, then use existing concurrency-token enforcement to prove stale competing assignment/token transitions cannot partially commit
- [ ] the hidden-shift regression asserts no live request, active assignment, or usable token remains after deactivation

### T005: Verify web behavior and version the enhancement

**File(s)**: `tests/VolunteerCoordinator.IntegrationTests/AuthorizationIntegrationTests.cs`; `VERSION`
**Type**: Modify
**Depends**: T003, T004
**Acceptance**:
- [ ] an allowlisted coordinator can POST assignment cancellation and receives redirect/textual feedback
- [ ] anonymous and non-allowlisted callers remain unable to execute coordinator cancellation
- [ ] root `VERSION` is `0.4.0`
- [ ] formatting, Release build, and the full isolated-PostgreSQL test suite pass

---

## Dependency Graph

