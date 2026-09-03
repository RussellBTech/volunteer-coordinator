# Tasks: Generate Recurring Shift Series in Local Civil Time

**Issue**: #20
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Recurrence core | 2 | [ ] |
| Corrections | 2 | [ ] |
| Presentation | 1 | [ ] |
| Verification | 2 | [ ] |
| **Total** | 7 | |

---

## Prerequisites

- Issue #13 supplies atomic protected-workflow resolution.
- Issue #15 supplies group IANA settings and local-time resolution.
- Issue #17 supplies guided previews/consequence interaction.
- Integrate issue #19 correction delivery when present.

---

## Phase 1: Recurrence Core

### T001: Model series revisions and occurrence identity

**File(s)**: new Domain series/revision/occurrence types; `VolunteerCoordinatorDbContext`; `IWorkflowStore`; `EfWorkflowStore`; new EF Core migration
**Type**: Create / Modify
**Depends**: #15
**Acceptance**:
- [ ] series, immutable effective revisions, and unique per-local-date occurrences are separate from authoritative concrete shifts
- [ ] daily/weekly interval, weekdays, local start, elapsed duration, 4–26 horizon, zone snapshot, overlap choice, content, and slots are validated
- [ ] Generated/Needs review/Skipped and exception invariants plus one-to-one Shift relation are database-enforced where practical
- [ ] revisions/occurrences are retained and cannot cascade-delete workflow history

### T002: Generate and top up bounded concrete shifts

**File(s)**: recurrence Application models/service; store batch/lock methods; generation hosted service; Web composition/options
**Type**: Create / Modify
**Depends**: T001
**Acceptance**:
- [ ] DateOnly/TimeOnly enumeration covers daily every 1–4 days and weekly every 1–4 weeks on selected weekdays
- [ ] overlap uses stored first/second UTC candidate; gap creates visible Needs review; end is start UTC plus elapsed duration
- [ ] preview/create generate default 12-week and configured 4–26 week horizons; absolute cap is 52 weeks
- [ ] startup/daily worker locks ordered series, pauses zone-mismatched series, inserts only missing dates, and survives one-series failure
- [ ] concurrent/manual/worker generation is idempotent and creates no duplicate shift/slots/audits

---

## Phase 2: Corrections

### T003: Preserve occurrence exceptions and resolve DST gaps

**File(s)**: shift command integration; occurrence exception/resolution Application methods; coordinator occurrence Razor Pages
**Type**: Modify / Create
**Depends**: T002, #13, #15
**Acceptance**:
- [ ] one-occurrence edit/deactivation marks exception atomically and never affects adjacent occurrences
- [ ] Needs review names the missing local time and permits only explicit valid local replacement or reasoned skip
- [ ] resolved gap creates one ordinary unpublished Shift; skipped occurrence stays visible/audited
- [ ] later generation/revisions never overwrite exceptions, skipped rows, or protected workflow state

### T004: Apply effective future revisions safely

**File(s)**: revision preview/command models; `VolunteerCoordinatorService`; store lock/batch methods; #13/#19 integration
**Type**: Modify / Create
**Depends**: T003
**Acceptance**:
- [ ] new revision starts at selected local date and classifies all future rows eligible/protected/exception/review/skipped
- [ ] default reconciliation updates only eligible non-exceptions and detaches protected rows unchanged
- [ ] stale classification/version rejects the whole revision transaction without partial shift change
- [ ] explicit per-occurrence resolve-and-replace reuses #13, previews people/links, and keeps notification failure independent
- [ ] group-zone mismatch pauses top-up until explicit adoption revision previews old/new local and UTC results

---

## Phase 3: Presentation

### T005: Add guided recurring schedule workspace

**File(s)**: `/Coordinator/Recurring` Razor Pages; coordinator home/navigation; shared previews/status components; `wwwroot/css/site.css`
**Type**: Create / Modify
**Depends**: T002, T003, T004, #17
**Acceptance**:
- [ ] Daily/Weekly paths use familiar local fields, weekdays, elapsed duration, horizon, and first/second overlap wording without RRULE/cron/UTC entry
- [ ] preview shows actual upcoming dates, times, offsets, roles, gaps, elapsed/local-end differences, and publication state before save
- [ ] detail groups Needs review, Unpublished, Published, Protected exceptions, and Skipped with one primary next action
- [ ] one-vs-following, zone adoption, resolution, and publication use server consequence preview/no-mutation back
- [ ] keyboard/assistive/320px layout preserves labels/order/actions without drag, hover, raw IDs, or color-only state

---

## Phase 4: Verification

### T006: Add recurrence, revision, and concurrency coverage

**File(s)**: `tests/VolunteerCoordinator.UnitTests/`; `tests/VolunteerCoordinator.IntegrationTests/`
**Type**: Create / Modify
**Depends**: T001, T002, T003, T004
**Acceptance**:
- [ ] deterministic tests cover patterns/anchors/week masks, default/config/max horizons, spring gap, both fall choices, elapsed duration, and no-DST zone
- [ ] PostgreSQL tests prove unique idempotency under two workers, independent occurrences, exception persistence, protected detachment, zone pause/adoption, and revision rollback
- [ ] explicit held-lock tests cover generator versus edit/deactivation/publication and correction versus new request/assignment
- [ ] all-or-none publication validates all selected rows before any Publish/audit mutation and reports every local blocker

### T007: Verify guided recurring lifecycle and version

**File(s)**: Web integration/browser tests; actual Web surface; `VERSION`
**Type**: Modify / Verify
**Depends**: T005, T006
**Acceptance**:
- [ ] increment the implementation branch's current root version by one minor component
- [ ] browser creates Daily and multi-weekday Weekly series, previews dates, resolves/skips a gap, publishes a range, edits one occurrence, and changes following occurrences
- [ ] protected assignment/request remains an unchanged exception until explicit #13 resolution; nearby occurrence state remains unchanged
- [ ] group-zone change visibly pauses generation and explicit adoption resumes correct top-up
- [ ] formatting, Release build, full isolated-PostgreSQL suite, migrations, worker smoke, and actual mobile/keyboard Web scenario pass

---

## Dependency Graph

```text
#15 ──▶ T001 ──▶ T002 ──▶ T003 ──▶ T004 ──▶ T005 ──▶ T007
                         │          │         │
#13 ─────────────────────┘          │         │
#17 ────────────────────────────────┘         │
T001 ──▶ T006 ◀── T002,T003,T004 ────────────┘
```

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #20 | 2026-09-03 | Initial feature spec |
