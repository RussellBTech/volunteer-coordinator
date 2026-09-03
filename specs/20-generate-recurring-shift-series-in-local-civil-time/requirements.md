# Requirements: Generate Recurring Shift Series in Local Civil Time

**Issue**: #20
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As a** volunteer coordinator
**I want** to define a regular service position once and generate future occurrences safely
**So that** weekly meetings do not require repetitive UTC data entry

---

## Background

Every shift is currently an unrelated UTC interval created and published individually. Regular group commitments need a stable local wall-clock start across daylight-saving changes, bounded future generation, independent occurrence coverage, and safe series corrections without weakening the coordinator exception rules.

The approved series supports daily intervals and weekly intervals with one or more selected weekdays. It automatically maintains a rolling 12-week horizon, allows 4–26 configured weeks, and hard-caps any generation request at 52 weeks. A series stores the group IANA zone snapshot and explicit first/second overlap choice. Nonexistent spring-forward starts become visible `Needs review` occurrences rather than moving or disappearing. Configured duration is real elapsed time added after the start resolves to UTC.

Future corrections create effective-dated revisions. Protected occurrences remain unchanged as explicit exceptions by default. Selected publication is all-or-nothing. A later group-zone change pauses top-up until each series explicitly reviews/adopts the new zone.

---

## Acceptance Criteria

### AC1: Define and maintain a recurring series

**Given** the group time zone is configured
**When** a coordinator previews and creates a daily-interval or weekly-weekday series with local start, elapsed duration, slots, and a 4–26 week horizon
**Then** traceable concrete shift occurrences are generated idempotently through the horizon with correct UTC instants, and a daily worker keeps that bounded horizon filled

### AC2: Explicit daylight-saving recurrence

**Given** a series crosses a daylight-saving transition
**When** an ambiguous local start is generated
**Then** the stored first/second occurrence choice is applied and audited, while a nonexistent local start creates one visible Needs review occurrence that cannot publish until the coordinator resolves or explicitly skips it

### AC3: Independent occurrence state

**Given** requests, assignments, publication, or a manual exception exist on one occurrence
**When** another occurrence is edited, deactivated, resolved, skipped, published, or regenerated
**Then** the first occurrence's shift, workflow, access, notification, and audit state remain unchanged

### AC4: Safe future-series correction

**Given** a coordinator creates a new series revision effective on a selected occurrence
**When** future occurrences are reconciled
**Then** eligible non-exception occurrences adopt the revision idempotently, protected occurrences remain unchanged and become detached exceptions by default, and an explicit issue-#13 consequence flow is required to resolve and replace any protected occurrence

### AC5: Atomic bulk publication

**Given** a coordinator selects a reviewed occurrence range
**When** they confirm publication
**Then** every selected eligible concrete shift publishes in one PostgreSQL transaction, or none publish and all blocking local dates/reasons are returned without raw identifiers

### AC6: Guided local-civil setup and zone adoption

**Given** a coordinator can describe a regular meeting in ordinary local terms, or the group zone has changed
**When** they use series create/review/adopt flows
**Then** actual upcoming local dates, times, roles, DST exceptions, publication state, and affected/protected counts are previewed before saving, and future top-up never silently switches a series to another zone

---

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Add coordinator-owned `RecurringShiftSeries`, immutable effective-dated revisions, and per-local-date occurrence records separate from concrete `Shift`. | Must | Concrete shifts remain authoritative for coverage/workflow. |
| FR2 | Support daily every 1–4 days and weekly every 1–4 weeks on one or more weekdays, anchored to a local start date, with local start time, elapsed duration, title/location/volunteer instructions/internal notes, and slot structure. | Must | No monthly/natural-language rules. |
| FR3 | Resolve each start in the series IANA zone snapshot; store first/second overlap policy, create Needs review for gaps, then add elapsed duration to resolved UTC start. | Must | Never silently shift, duplicate, or omit an occurrence. |
| FR4 | Maintain a default rolling 12-week horizon daily; coordinator range 4–26 weeks and absolute generation cap 52 weeks. Enforce unique series/local-date occurrence and idempotent reconciliation. | Must | Bounded batches and deterministic order. |
| FR5 | Preserve independent occurrence state. Manual occurrence edits/cancellation/resolution mark an exception that later series revisions/generation never overwrite. | Must | #13 governs destructive resolution. |
| FR6 | Apply effective-dated revision changes to eligible future non-exception occurrences. Protected occurrences stay unchanged/detached by default; explicit resolve-and-replace previews list people, links, and notifications. | Must | No implicit cancellation. |
| FR7 | Pause top-up when the configured group zone differs from the series snapshot. Coordinator adoption previews changed UTC instants and creates a revision; generated concrete UTC occurrences remain fixed until explicitly reconciled. | Must | #15 group-zone semantics remain authoritative. |
| FR8 | Publish selected reviewed concrete occurrences atomically under deterministic locks and expected versions; collect every blocking date/reason before mutation. | Must | Needs-review/skipped/stale/protected-invalid rows block selection. |
| FR9 | Add guided coordinator create/edit/occurrence review, gap resolution/skip, single-occurrence exception, zone-adoption, and bulk-publication pages using #17 interaction rules. | Must | No external documentation or UTC entry. |
| FR10 | Add deterministic domain, PostgreSQL, worker, and Web coverage for patterns, boundaries, DST, horizon top-up, revisions, exceptions, conflicts, all-or-none publication, and audit/notification effects. | Must | Fixed zones/clocks; isolated PostgreSQL. |

---

## Out of Scope

- Volunteer recurring enrollment or assignment across a series
- Monthly, ordinal, holiday, exclusion-calendar, or natural-language recurrence rules
- External calendar synchronization or feeds
- Unbounded occurrence generation
- Automatic destructive resolution of protected commitments

---

## Versioning

The `enhancement` label requires one minor version increment from the implementation branch's current root `VERSION`.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #20 | 2026-09-03 | Initial feature spec |
