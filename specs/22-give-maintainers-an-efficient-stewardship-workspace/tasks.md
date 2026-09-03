# Tasks: Give Maintainers an Efficient Stewardship Workspace

**Issue**: #22
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Work and roster | 2 | [ ] |
| History and access | 2 | [ ] |
| Performance | 1 | [ ] |
| Verification | 2 | [ ] |
| **Total** | 7 | |

---

## Prerequisites

Issues #14–#21 supply operational setup, local context, privacy, guided UI, delivery, recurrence, and recurring participation. Extend those surfaces; do not duplicate them.

---

## Phase 1: Work and Roster

### T001: Build one prioritized coordinator work projection

**File(s)**: coordinator work Application models/service/store methods; Home integration; `/Coordinator/Work` Razor Page; attention options
**Type**: Create / Modify
**Depends**: #17, #19, #20, #21
**Acceptance**:
- [ ] shared classifier supplies Home counts/examples and 50-row Work pages for every approved actionable category
- [ ] validated thresholds are Urgent 24 hours and Soon 72 hours; severity/category/due ordering and dedup are deterministic
- [ ] only live/future actionable state appears and each row links to one narrow existing intervention
- [ ] fixed category/severity filters and empty caught-up state use plain text; no bulk mutation or vanity metric is added

### T002: Replace full roster with bounded private search

**File(s)**: Volunteer normalized-name domain/migration/index; search Application/store method; #17 known-volunteer Razor flow
**Type**: Create / Modify
**Depends**: #16, #17
**Acceptance**:
- [ ] antiforgery POST requires 3–100 prefix characters and returns at most 10 active non-anonymized matches
- [ ] result contains only ID/name/full email; no phone/history/nonmatch/total count or URL/log search term
- [ ] exact email then name/email stable ordering uses indexed normalized fields and one SQL command
- [ ] selection reloads/locks/revalidates volunteer and stale/anonymized records fail generically with preserved search input

---

## Phase 2: History and Access

### T003: Add structured paged audit history

**File(s)**: `AuditEntry`; all audit creation call sites; EF mapping/migration; audit Application/store queries; `/Coordinator/Audit` History view; protected cursor service
**Type**: Modify / Create
**Depends**: #15, #16, #19, #20, #21
**Acceptance**:
- [ ] new writes carry nullable structured ShiftId/VolunteerId; migration backfills only deterministic valid relationships/JSON and preserves DetailJson
- [ ] indexed filters cover local date, actor, shift, volunteer reference, and fixed plain action category
- [ ] 50-row descending keyset pages use filter-bound protected previous/next cursors and no offset/total count
- [ ] invalid/stale cursor safely returns newest matching page with plain message
- [ ] every known action has human local summary; fallback exposes no raw GUID/action/JSON/token/provider payload

### T004: Expose safe coordinator handoff diagnostics

**File(s)**: `/Coordinator/Access` Razor Page; authorization/audit Application method; layout/home links; #14 operations guidance
**Type**: Create / Modify
**Depends**: #14, #17
**Acceptance**:
- [ ] page shows OIDC completeness, distinct normalized allowlisted emails/count, current identity, latest current-list verification, and readiness without secrets
- [ ] authorized antiforgery Verify my access writes one `CoordinatorAccessVerified` audit and changes no allowlist/config
- [ ] Handoff ready requires complete OIDC, two current distinct emails, and two current verification records
- [ ] visible add, independent-sign-in, verify, remove, and re-verify order prevents shared credentials/schedule loss
- [ ] removed historical actors stay audited but do not count as ready

---

## Phase 3: Performance

### T005: Enforce set-based coordinator query budgets

**File(s)**: `IWorkflowStore`; `EfWorkflowStore`; coordinator page queries; integration command-count fixtures/index plans
**Type**: Modify
**Depends**: T001, T002, T003
**Acceptance**:
- [ ] Home/Work/History stay at or below 6 SQL commands, Requests/Coverage/Messages at or below 5, Search at 1
- [ ] budgets remain unchanged at one and ten times rendered page volume over at least 1,000 volunteers, 5,000 shifts, and 10,000 audits
- [ ] request/coverage/history/roster loops, lazy loading, unbounded includes/materialization, and cache masking are absent
- [ ] PostgreSQL plans use approved urgency/search/filter/keyset indexes; timing is reported but not a flaky gate

---

## Phase 4: Verification

### T006: Add workspace correctness and privacy coverage

**File(s)**: `tests/VolunteerCoordinator.UnitTests/`; `tests/VolunteerCoordinator.IntegrationTests/`
**Type**: Create / Modify
**Depends**: T001, T002, T003, T004, T005
**Acceptance**:
- [ ] tests cover all severity/category boundaries/order/dedup and Home/Work count agreement
- [ ] search tests cover minimum/maximum input, result cap/order, anonymized exclusion, stale selection, no phone/history, and no log/query leak
- [ ] audit tests cover migration/backfill, every filter/category, local DST dates, insert-between-pages stability, cursor tamper/filter mismatch, neutral removed reference, and fallback
- [ ] access tests cover incomplete/one/two allowlist, current user, verification, removed actor, authorization, antiforgery, and no secret values
- [ ] command-budget tests use representative and scaled deterministic PostgreSQL fixtures

### T007: Verify no-training stewardship and version

**File(s)**: Web integration/browser tests; actual Web surface; `VERSION`
**Type**: Modify / Verify
**Depends**: T006
**Acceptance**:
- [ ] increment the implementation branch's current root version by one minor component
- [ ] a rotating coordinator follows only Home/Work text to resolve request, coverage, message, withdrawal, and recurrence-review items
- [ ] browser searches/selects known volunteer, investigates filtered multi-page history, and completes two-identity handoff verification
- [ ] keyboard/assistive/320px checks preserve one action, filters, inputs, cursors, states, and no technical/personal leakage
- [ ] formatting, Release build, full isolated-PostgreSQL suite, migrations, query-budget fixture, and actual Web smoke pass

---

## Dependency Graph

```text
T001 ──┐
T002 ──┼──▶ T005 ──▶ T006 ──▶ T007
T003 ──┤
T004 ──┘
```

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #22 | 2026-09-03 | Initial feature spec |
