# Tasks: Present Commitments in Local Time With Complete Context

**Issue**: #15
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Settings and time | 2 | [ ] |
| Commitment context | 2 | [ ] |
| Verification | 2 | [ ] |
| **Total** | 6 | |

---

## Phase 1: Settings and Time

### T001: Persist audited group time-zone settings

**File(s)**: `src/VolunteerCoordinator.Domain/Settings/GroupSettings.cs`; `src/VolunteerCoordinator.Application/Ports/IWorkflowStore.cs`; `src/VolunteerCoordinator.Application/VolunteerCoordinatorService.cs`; `src/VolunteerCoordinator.Infrastructure/Persistence/VolunteerCoordinatorDbContext.cs`; `src/VolunteerCoordinator.Infrastructure/Persistence/EfWorkflowStore.cs`; new EF Core migration
**Type**: Create / Modify
**Depends**: None
**Acceptance**:
- [ ] exactly one group settings aggregate stores a valid IANA zone ID and uses `xmin` optimistic concurrency
- [ ] initial configuration and confirmed later changes commit with the normalized coordinator and matching audit action
- [ ] changed-zone audit details include only old/new identifiers and existing UTC shift instants are not rewritten
- [ ] stale settings writes fail without partial state or audit mutation
- [ ] the migration does not seed a guessed zone

### T002: Resolve local schedule input explicitly

**File(s)**: focused Application time-input/resolution models; `src/VolunteerCoordinator.Application/VolunteerCoordinatorService.cs`; coordinator schedule Create/Edit page models and views
**Type**: Create / Modify
**Depends**: T001
**Acceptance**:
- [ ] forms accept familiar local start/end values and never ask for UTC conversion
- [ ] resolution uses `DateTimeKind.Unspecified`, the persisted IANA zone, and expected settings version
- [ ] DST gaps are rejected with plain-language correction guidance
- [ ] each ambiguous start/end requires a choice between two labelled offsets before mutation
- [ ] stale/non-candidate offsets and concurrent zone changes fail without mutation
- [ ] UTC ordering and elapsed duration remain authoritative across DST transitions

---

## Phase 2: Commitment Context

### T003: Add private notes and volunteer instructions boundary

**File(s)**: `src/VolunteerCoordinator.Domain/Schedules/Shift.cs`; shift DTOs/commands; coordinator schedule page models/views; new EF Core migration from T001
**Type**: Modify
**Depends**: T001
**Acceptance**:
- [ ] nullable volunteer instructions use the existing 1,000-character normalization and validation behavior
- [ ] existing notes remain persisted and are labelled `Internal coordinator notes`
- [ ] public/application commitment models expose volunteer instructions but never internal notes
- [ ] shift create/edit audits do not copy either free-text field into audit details

### T004: Render one complete local commitment everywhere

**File(s)**: `src/VolunteerCoordinator.Application/Models/`; relevant projections in `VolunteerCoordinatorService`; `src/VolunteerCoordinator.Web/Presentation/GroupTimeFormatter.cs`; `src/VolunteerCoordinator.Web/Pages/Shared/_CommitmentDetails.cshtml`; public and coordinator commitment Razor Pages
**Type**: Create / Modify
**Depends**: T002, T003
**Acceptance**:
- [ ] one shared projection carries title, UTC start/end, IANA zone, location, slot, and volunteer instructions without per-row settings queries
- [ ] one formatter/partial renders deterministic local start/end, true elapsed duration, abbreviation, numeric offset, semantic UTC `<time>` values, and labelled optional-field text
- [ ] browse, request, status, action, schedule, request queue, assign/reassign, action links, and coverage use the shared context
- [ ] output does not depend on browser locale, JavaScript, host local zone, color, or icons
- [ ] cross-date and DST-changing commitments remain unambiguous

---

## Phase 3: Verification

### T005: Cover bootstrap, DST, privacy, and concurrency

**File(s)**: `tests/VolunteerCoordinator.UnitTests/`; `tests/VolunteerCoordinator.IntegrationTests/`
**Type**: Create / Modify
**Depends**: T001, T002, T003
**Acceptance**:
- [ ] fixed-zone tests cover invalid IANA IDs, `America/New_York` 2026 gap/overlap, both overlap choices, stale offsets, cross-midnight duration, and a no-DST zone
- [ ] PostgreSQL tests cover migration, singleton enforcement, `xmin` conflict, atomic settings/audit updates, instructions, and unchanged UTC instants after zone correction
- [ ] public GET/POST routes before configuration produce one textual unavailable state for valid/invalid identifiers and change no request, assignment, token, notification, or audit state
- [ ] settings and internal notes never appear in public DTOs or rendered public responses

### T006: Verify actual pages and version enhancement

**File(s)**: Web integration tests; `VERSION`
**Type**: Modify / Verify
**Depends**: T004, T005
**Acceptance**:
- [ ] increment the implementation branch's current root version by one minor component
- [ ] an authorized coordinator configures the zone, creates ordinary and ambiguous commitments through local controls, explicitly resolves the overlap, and confirms the audit trail
- [ ] browser verification covers every public/coordinator commitment surface and textual/semantic details at desktop and narrow mobile widths
- [ ] changing the zone after confirmation leaves UTC database values fixed and updates all local displays
- [ ] formatting, Release build, the full isolated-PostgreSQL suite, migration application, and actual Web smoke scenario pass

---

## Dependency Graph

```text
T001 ──┬──▶ T002 ──┐
       └──▶ T003 ──┼──▶ T004 ──▶ T006
T001 ──▶ T005 ─────┘
T002 ──▶ T005
T003 ──▶ T005
```

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #15 | 2026-09-03 | Initial feature spec |
