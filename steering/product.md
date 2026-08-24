# Volunteer Coordinator Product Steering

This document defines the product vision, target users, workflows, and success outcomes. All product work must align with it.

## Mission

**Volunteer Coordinator keeps time-bound service shifts covered by making open work visible, giving volunteers clear self-service actions, and giving coordinators authoritative schedule and coverage state.**

## Target Users

### Volunteers

Volunteers are occasional, often mobile users. They need low-friction discovery of available work, clear request status, and safe confirmation, decline, and cancellation actions without maintaining an account.

### Coordinators

Coordinators own schedule setup and publication, request decisions, assignments and exceptions, and current coverage visibility. Their actions require authenticated access and an audit trail.

## Core Value Proposition

1. **Visible open work** — Volunteers can find published primary and backup slots that need coverage.
2. **Clear self-service** — Volunteers can request work, see status, and safely confirm, decline, or cancel.
3. **Authoritative coordination** — Coordinators can establish the real schedule, resolve requests and assignments, and identify coverage risk from one system of record.

## Initial Product Workflows

1. Coordinators create the available shift schedule, correct it, and publish it.
2. Volunteers browse open primary or backup slots.
3. Volunteers submit requests with their contact details.
4. Coordinators approve, reject, assign, or reassign requests and assignments.
5. Volunteers confirm, decline, or cancel their assignments.
6. Coordinators see uncovered and unconfirmed shifts and intervene.

## First-Release Shift Setup

Shift setup is a first-release capability. Authenticated coordinators can create, edit, deactivate, and correct the available shifts in the application before publishing them. The initial deployment may contain zero shifts and must show a clear coordinator setup state that leads to shift creation. Schedule values must come from coordinator entry; they must not be hardcoded or inferred from unrelated local data.

## Product Invariants

- Schedule, request, and assignment state in the database is authoritative.
- Notification delivery never determines whether a workflow state transition succeeded.
- Every user-visible state has text in addition to any color or icon.
- Sensitive volunteer actions use expiring, single-use links.
- Coordinator actions are authenticated and auditable.

## Success Outcomes

- A coordinator can enter the real available shifts without code or database access and revise them after launch.
- A volunteer can complete the browse → request → status → confirm/decline/cancel journey.
- A coordinator can complete shift setup → publish → approve/assign → monitor coverage → intervene.
- A notification failure leaves the underlying workflow state correct and visible.
- Upcoming uncovered or unconfirmed shifts are directly discoverable.

## Product Principles

| Principle | Product decision |
|-----------|------------------|
| State before notification | Persist and expose workflow truth independently of message delivery. |
| Explicit before implicit | Show status and consequences; do not hide state transitions behind labels or color. |
| Coordinator-owned schedules | Let authenticated coordinators establish and revise schedule data in the product. |
| Low-friction volunteer access | Require no persistent volunteer account for the first releasable workflow. |
| Coverage visibility | Prioritize actionable uncovered and unconfirmed work over administrative reporting. |

## Lifecycle Commands

| Need | Command |
|------|---------|
| Draft a scoped issue | `/sdlc-draft-issue [need]` |
| Write the issue specification | `/sdlc-write-spec #N` |
| Onboard a project | `/sdlc-onboard-project` |
| Reconcile an existing project | `/sdlc-upgrade-project` |
| Execute approved issue specifications | `/sdlc-execute [#N …]` |
| Inspect lifecycle status | `/sdlc-status` |

## References

- Technical constraints: `steering/tech.md`
- Code organization: `steering/structure.md`
