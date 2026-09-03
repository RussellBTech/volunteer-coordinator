# Tasks: Guide Nontechnical Coordinators Through Setup and Routine Work

**Issue**: #17
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Home | 2 | [ ] |
| Safe actions | 2 | [ ] |
| Language and access | 1 | [ ] |
| Verification | 2 | [ ] |
| **Total** | 7 | |

---

## Prerequisites

- Issue #13 implementation supplies atomic shift deactivation and coordinator assignment cancellation.
- Issue #15 implementation supplies group time-zone settings, local schedule input, and shared commitment context.

---

## Phase 1: Home

### T001: Project setup and routine attention state

**File(s)**: `src/VolunteerCoordinator.Application/Models/`; `src/VolunteerCoordinator.Application/VolunteerCoordinatorService.cs`; `src/VolunteerCoordinator.Application/Ports/IWorkflowStore.cs`; `src/VolunteerCoordinator.Infrastructure/Persistence/EfWorkflowStore.cs`
**Type**: Create / Modify
**Depends**: #13, #15
**Acceptance**:
- [ ] one bounded authoritative projection derives setup mode, ordered steps, routine counts, and at most three examples per category
- [ ] setup progress comes from settings/shift state and persists no wizard flags
- [ ] message attention includes only failures tied to commitments whose shifts have not ended
- [ ] all queries use `_clock.UtcNow`, batch related reads, and avoid per-row database calls
- [ ] fixed allowlisted filters map to plain-language categories without entity IDs

### T002: Add coordinator home and direct navigation

**File(s)**: new `/Coordinator/Index` Razor Page; `src/VolunteerCoordinator.Web/Pages/Shared/_Layout.cshtml`; login handlers; coordinator attention target pages
**Type**: Create / Modify
**Depends**: T001
**Acceptance**:
- [ ] successful login and authenticated home navigation lead to `/Coordinator`
- [ ] empty setup shows five ordered steps and exactly one primary next action through first publication
- [ ] the request-approval step explains current behavior and adds no configurable/self-scheduling policy
- [ ] routine nonzero cards link to filtered resolution pages; all zero renders the caught-up state
- [ ] direct Schedule, Requests, Coverage, Messages, and History routes remain bookmarkable

---

## Phase 2: Safe Actions

### T003: Add authoritative consequence previews

**File(s)**: Application preview DTOs/queries; publish/deactivate/cancel/replacement Razor Pages; shared consequence preview partial
**Type**: Create / Modify
**Depends**: T001
**Acceptance**:
- [ ] previews show affected people, local dates, slots, and exact consequences for publish, deactivation, cancellation, and replacement
- [ ] every flow offers `Go back without changes` and executes no command on back
- [ ] final antiforgery POST reloads authoritative preview state and rejects stale versions/affected sets before mutation
- [ ] explicit confirmation labels name the result; no browser-only dialog is used
- [ ] no new bulk mutation is introduced, while the shared preview contract is reusable by later bulk work

### T004: Split known and new volunteer assignment

**File(s)**: assignment application commands/models; `/Coordinator/Assignments/Assign` and focused choose/new/review Razor Pages
**Type**: Create / Modify
**Depends**: T003
**Acceptance**:
- [ ] landing offers separate `Choose known volunteer` and `Add someone new` paths
- [ ] known selection displays name/email but posts a stable ID and never requires retyping contact data
- [ ] missing/anonymized stale selections fail generically and no contact value enters URL/log output
- [ ] new and known replacement both use the same consequence-review/final-confirmation contract
- [ ] changing details or validation failure preserves valid bound input without workflow mutation

---

## Phase 3: Language and Access

### T005: Standardize routine language and accessibility

**File(s)**: coordinator Razor Pages; shared validation/status components; audit projection/view; new `/Coordinator/Messages` read-only page; `wwwroot/css/site.css`
**Type**: Create / Modify
**Depends**: T002, T003, T004
**Acceptance**:
- [ ] visible vocabulary uses requests to review, open commitments, waiting for confirmation, confirmed, and messages not sent consistently
- [ ] history shows local human event summaries without raw GUIDs, JSON, action codes, UTC instructions, or database/provider wording
- [ ] message warnings explain manual follow-up and the read-only list exposes only current/future actionable failures; no retry/resend is added
- [ ] form errors appear beside fields and in `All` summaries, preserve input, and describe reload/review for concurrency
- [ ] semantic order, 44px targets, keyboard access, and 320px layout require no color, hover, drag, hidden gesture, or horizontal page scroll

---

## Phase 4: Verification

### T006: Add workflow and presentation behavior coverage

**File(s)**: `tests/VolunteerCoordinator.IntegrationTests/`; focused unit tests where pure presentation mapping warrants them
**Type**: Create / Modify
**Depends**: T001, T003, T004, T005
**Acceptance**:
- [ ] PostgreSQL tests cover every setup/routine state, message time boundary, bounded examples, and known-volunteer selection
- [ ] stale publish/deactivate/cancel/replace confirmations and no-mutation back paths preserve authoritative state
- [ ] Web tests cover login redirect, authorization, antiforgery, filters, input preservation, field/summary errors, and technical-leakage regression
- [ ] tests prove no self-scheduling policy, bulk mutation, or message retry/resend was added

### T007: Verify first-time and mobile operation and version

**File(s)**: actual Web surface; `VERSION`
**Type**: Modify / Verify
**Depends**: T006
**Acceptance**:
- [ ] increment the implementation branch's current root version by one minor component
- [ ] a clean-database coordinator follows only visible application text through time zone, first shift, review, publication, and routine attention
- [ ] browser verification at desktop and 320px completes keyboard-only known/new assignment and every consequence preview/confirmation/back flow
- [ ] accessibility inspection confirms landmarks, heading/focus order, labels, summaries, status announcements, and non-color text
- [ ] formatting, Release build, full isolated-PostgreSQL suite, migrations, and actual Web smoke scenario pass

---

## Dependency Graph

```text
#13 ──┐
      ├──▶ T001 ──▶ T002 ──┐
#15 ──┘          │          ├──▶ T005 ──▶ T006 ──▶ T007
                 └──▶ T003 ─┤
                            └──▶ T004 ─────────────┘
```

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #17 | 2026-09-03 | Initial feature spec |
