# Design: Generate Recurring Shift Series in Local Civil Time

**Issue**: #20
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Overview

Add recurrence as a coordinator-owned planning layer above existing concrete shifts. Coverage, volunteer requests, assignments, capabilities, notifications, and audits continue to target ordinary `Shift`/`ShiftSlot` rows. A series and its immutable revisions decide which concrete occurrences should exist; they never become an alternate workflow authority.

Generation enumerates bounded local calendar dates, resolves each start through issue #15's IANA/DST resolver, and creates ordinary UTC shifts. A unique occurrence identity makes generation/top-up idempotent. Manual exceptions and protected live workflow state are explicit and never overwritten by routine reconciliation.

Prerequisites: #13 atomic exception resolution, #15 group-local input/time-zone settings, #17 consequence-preview interaction, and #19 correction/cancellation delivery when present.

---

## Domain Model

### `RecurringShiftSeries`

Fields: `Id`, `IsActive`, `CurrentRevisionNumber`, `CreatedAtUtc`, `UpdatedAtUtc`, and PostgreSQL `xmin` `Version`. It owns ordered revisions conceptually but domain navigation need not materialize all history for every command. Deactivation stops top-up; it does not automatically deactivate concrete shifts.

### `RecurringShiftSeriesRevision`

Immutable after creation:

- `Id`, `SeriesId`, positive `RevisionNumber`, `EffectiveLocalDate`;
- title, location, volunteer instructions, internal coordinator notes;
- primary plus 0–2 backup-slot structure;
- `RecurrenceKind` Daily or Weekly;
- `Interval` 1–4;
- weekly day mask containing at least one day for Weekly and empty for Daily;
- `AnchorLocalDate`, `LocalStartTime`;
- positive elapsed `DurationMinutes` with the existing shift-range business constraints;
- `HorizonWeeks` 4–26, default 12;
- IANA `TimeZoneId` snapshot;
- `AmbiguousTimeChoice` FirstOccurrence or SecondOccurrence;
- normalized coordinator actor and created UTC.

Unique `(SeriesId, RevisionNumber)` and ordered effective-date constraints prevent two competing revisions for the same boundary. New revisions, rather than mutation, preserve what produced each occurrence and audit explanation.

### `RecurringShiftOccurrence`

Fields: `Id`, `SeriesId`, `RevisionId`, `LocalDate`, nullable `ShiftId`, `OccurrenceStatus` (`Generated`, `NeedsReview`, `Skipped`), `IsException`, nullable resolution/skip actor/time/reason, `CreatedAtUtc`, and concurrency version. Unique `(SeriesId, LocalDate)` is the idempotency boundary. A unique nullable `ShiftId` makes the concrete relation one-to-one.

`Generated` requires a concrete Shift. `NeedsReview` has no Shift until resolved. `Skipped` has no Shift and an explicit coordinator actor/reason. `IsException` means routine revision reconciliation/top-up cannot overwrite it; its concrete shift remains independently editable through existing commands.

Add nullable `RecurringOccurrenceId`/one-to-one relation according to the least cyclic EF mapping. Deleting series/revision/occurrence rows is restricted; history stays auditable.

---

## Pattern Enumeration and Time Resolution

Enumerate dates deterministically from `AnchorLocalDate` through an inclusive local horizon date:

- Daily: include dates whose whole-day distance from anchor is nonnegative and divisible by Interval.
- Weekly: define anchor's ISO Monday as week zero; include selected weekdays in weeks whose nonnegative week distance is divisible by Interval.

Reject selected weekdays/dates before anchor. Use `DateOnly`/`TimeOnly`, not UTC arithmetic, for recurrence selection.

For each date, combine with `LocalStartTime` as `DateTimeKind.Unspecified` and resolve in the revision's IANA zone:

- invalid/gap: insert/update one `NeedsReview` occurrence; do not create a Shift;
- ambiguous/overlap: order candidate UTC instants ascending and use stored FirstOccurrence/SecondOccurrence;
- ordinary: use the sole offset.

After resolving start UTC, compute `EndsAtUtc = StartsAtUtc + DurationMinutes`. Duration is real elapsed service time. The displayed local end may therefore differ by one wall-clock hour on a transition day; preview calls this out. Do not resolve a second local end or silently alter elapsed duration.

Each generated shift copies revision content and slot structure, remains unpublished, and receives `RecurringOccurrenceId`. Audit `RecurringOccurrenceGenerated` contains series/revision/occurrence/shift IDs, local date, zone, UTC instants, and DST choice, never notes/instructions text.

---

## Creation, Preview, and Horizon Top-Up

`PreviewRecurringSeriesAsync` accepts local form input and returns actual upcoming occurrences through the chosen horizon: local start/end/zone/offset, elapsed duration, roles, gap/overlap labels, and count. It performs no persistence. Preview output is signed by posted expected group-settings version and exact form values; final creation revalidates rather than trusts computed hidden dates.

`CreateRecurringSeriesAsync` validates the current group zone, creates series/revision, enumerates/generates the initial horizon in one transaction, writes one series audit plus per-gap/generated summaries, and leaves all concrete shifts unpublished for review. A gap does not fail creation because its explicit Needs review row prevents disappearance.

Add `RecurringSeriesGenerationHostedService` with a startup run and 24-hour interval. It finds active series ordered by ID and processes bounded batches in separate transactions. Target horizon is `group-local today + HorizonWeeks * 7 days`; never exceed 52 weeks from group-local today in an explicit request. Lock series row, reload latest applicable revisions, pause on group-zone mismatch, enumerate, and insert only missing unique local dates. Concurrent workers rely on row lock plus unique `(SeriesId, LocalDate)`.

Store `LastGeneratedThroughLocalDate` only as operational metadata, never authority; missing-date enumeration plus unique occurrence decides correctness. One series failure logs only series ID/safe category and does not stop later series.

---

## Single-Occurrence Exceptions and DST Gaps

Existing shift edit/deactivation routes detect `RecurringOccurrenceId` and preview `Change only this occurrence` versus `Change this and following occurrences`. Choosing one occurrence marks it `IsException=true` in the same transaction before applying the existing command. Later revisions never overwrite it.

A Needs-review page shows the nonexistent local start and explains the clock change. Coordinator choices:

- choose another valid local start for this occurrence, resolve through #15, create its concrete unpublished shift, and mark exception;
- explicitly skip this date with required plain-language reason and audit.

There is no automatic “move forward by the gap” or silent skip. Skipped dates remain visible in series history and previews.

Cancellation of one generated shift uses #13 deactivation and marks exception. It does not deactivate the series or adjacent occurrences.

---

## Effective-Dated Series Corrections

`Change this and following` creates a new immutable revision effective at the selected occurrence's local date. Preview enumerates existing occurrences and classifies each:

- eligible: non-exception, no pending request, no active assignment;
- protected: pending request or assigned/confirmed assignment;
- exception: already manually detached;
- needs review/skipped.

Default confirm reconciles only eligible occurrences. It edits/regenerates their ordinary Shift values and slot structure under existing constraints, changes their `RevisionId`, and leaves protected rows/Shift/workflow untouched while setting `IsException=true`. Existing exceptions remain untouched. Preview lists all protected dates/people/status and states they will keep their prior schedule.

A separate `Resolve and replace protected occurrence` flow operates on selected occurrences using #13 semantics and #17 consequence preview. Each selected occurrence is explicitly confirmed; it supersedes/cancels/invalidates access, applies the revision, sends #19 correction/cancellation intents, and audits. Routine series correction never selects this automatically.

Reconciliation locks occurrences/shifts/slots in ascending stable-ID order, revalidates the classification, and commits the revision plus eligible changes atomically. A stale row returns updated preview with no partial revision.

---

## Group Time-Zone Change

Each revision retains its IANA zone snapshot. When #15 changes group zone, existing concrete UTC shifts remain fixed and all active series whose snapshot differs become `Zone review required`; top-up pauses before generating another date.

The series page previews adopting the group zone: existing eligible unprotected future occurrences show old/new local and UTC values, gaps/overlaps, protected/exception dates remain fixed, and the stored overlap choice is confirmed or changed. Confirm creates a new effective revision. Nothing automatically switches zone or rewrites protected/exception occurrences.

The coordinator may leave a series paused while existing occurrences remain valid. Home from #17 surfaces the zone-review count as setup/attention work.

---

## Atomic Bulk Publication

Coordinator selects a contiguous local-date range within generated occurrences. Preview lists every occurrence and collects blockers: Needs review, Skipped, missing Shift, inactive/stale shift, ended shift, zone-review pause when selection includes unreconciled new occurrences, or expected-version mismatch.

Final `PublishRecurringOccurrencesAsync` reloads the exact selected occurrence IDs from server-derived range, locks concrete shifts/slots in ascending ID order, validates all rows first, and only then calls existing `Shift.Publish` and writes per-shift plus `RecurringOccurrencesPublished` batch audit in one transaction. Any blocker publishes none. The response lists every blocking local date/reason, no GUID/JSON.

Publication is not automatic. Newly topped-up occurrences remain unpublished until reviewed/selected.

---

## Web Experience

Add `/Coordinator/Recurring` list/create/review/detail/revision/occurrence pages following #17:

- separate Daily and Weekly choices;
- local date/time, interval, weekday checkboxes, elapsed duration, 4–26 week horizon, first/second overlap explanation;
- server-rendered preview of actual dates/roles/DST status before save;
- detail grouped into Needs review, Unpublished, Published, Protected exceptions, and Skipped;
- one primary next action; consequence previews and no-mutation back links;
- no cron/RRULE/UTC/offset entry, raw IDs, drag/drop, or color-only state.

At 320px use cards/labelled lists rather than wide recurrence tables. Keyboard and assistive text expose weekdays, states, range, blockers, and confirmation consequences.

---

## Security, Concurrency, and Privacy

- Every series page/command is CoordinatorOnly, antiforgery-protected, and audited with normalized actor.
- Preview GET/POST does not mutate; final commands recompute dates/classifications and check group/series/shift versions.
- Multi-row locks use deterministic order; unique occurrence constraints make worker and manual generation idempotent.
- Internal notes remain coordinator-only; audits/logs exclude note/instruction/contact content.
- Protected occurrence resolution reuses #13 token invalidation; #19 notification failures cannot roll back schedule state.

---

## Performance Considerations

Default generation is bounded to 12 weeks, configured to 26, hard-capped at 52. Enumerating at most 366 local dates per series is cheap; database work batches existing occurrence keys and inserts missing rows once. Worker batches series and commits separately.

Indexes cover active series, effective revisions, unique series/date, occurrence status, and nullable shift relation. List/preview projections batch workflow summaries rather than querying per occurrence.

---

## Testing Strategy

| Layer | Type | Coverage |
|-------|------|----------|
| Domain/Application | Unit | Daily/weekly interval enumeration, anchor boundaries, weekday mask, overlap selection, gap row, elapsed duration across DST, revision applicability. |
| PostgreSQL | Integration | Migration/constraints, idempotent concurrent generation, worker horizon, independent workflow state, protected classification, stale rollback, all-or-none publication. |
| Web | Integration/browser | Guided preview/create, mobile/keyboard, gap resolve/skip, one-vs-following, zone adoption, protected consequence, blocker list. |
| Cross-feature | Integration | #13 resolution, #15 zone change, #17 home attention, #19 correction delivery independence. |

Use fixed IANA zones including `America/New_York`, a no-DST zone, spring gaps, fall overlaps, interval boundaries, and group-zone changes. Never use host local time or unbounded current-date assumptions.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #20 | 2026-09-03 | Initial feature spec |
