# Requirements: Present Commitments in Local Time With Complete Context

**Issue**: #15
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As a** volunteer
**I want** every commitment shown in the group's local time with its duration, place, and instructions
**So that** I can decide and arrive without converting UTC or finding a separate message

---

## Background

The application correctly stores instants in UTC, but forms and pages expose those UTC values directly. Request status and assignment-action pages omit end time, location, and instructions. One durable group time-zone identity and explicit daylight-saving behavior are required before recurring schedules can be specified safely.

The approved model adds an audited singleton group setting selected by a coordinator, preserves UTC persistence, accepts ordinary local date/time input, rejects nonexistent wall times, and requires a choice between both offsets when a wall time repeats. A separate volunteer-visible instructions field is added; existing notes remain coordinator-only. Until the group zone is configured, public commitment pages show a uniform textual unavailable state and perform no commitment mutation rather than guessing a zone or displaying UTC.

---

## Acceptance Criteria

### AC1: Coordinator-configured group time zone

**Given** an authenticated coordinator selects a valid IANA time-zone identifier
**When** the setting is saved with its current expected version
**Then** the single PostgreSQL group setting and an audit naming the coordinator commit atomically, and every schedule form and commitment view uses that zone while persisted instants remain UTC

### AC2: Explicit daylight-saving interpretation

**Given** a coordinator enters a local start or end time that is invalid or ambiguous at a daylight-saving transition
**When** the shift is created or edited
**Then** a nonexistent time is rejected with plain-language correction guidance, and a repeated time is not saved until the coordinator explicitly chooses one of the two labelled UTC-offset interpretations

### AC3: Complete volunteer context

**Given** a volunteer browses openings, requests a slot, checks private request status, or inspects an assignment action
**When** a commitment is displayed
**Then** its local start, local end, duration, IANA zone/offset text, location, slot role, and volunteer-visible instructions are consistently available while coordinator-only notes remain private

### AC4: Accessible deterministic presentation

**Given** assistive technology, a narrow mobile viewport, or a browser with a different locale
**When** commitment details are rendered
**Then** server-rendered labels, semantic time values, and textual state communicate the complete commitment without relying on color, icons, browser locale, or manual UTC conversion

### AC5: Safe bootstrap and time-zone correction

**Given** the group zone is not configured, or a coordinator changes an existing zone
**When** a public commitment route or settings update is handled
**Then** unconfigured routes show the same explicit unavailable state without revealing token validity or mutating workflow state, and a zone change requires confirmation that existing UTC instants stay fixed while local displays change and audits the old and new zone identifiers

---

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Add one PostgreSQL-backed `GroupSettings` aggregate with an IANA `TimeZoneId`, optimistic concurrency, coordinator-only settings page, and `GroupTimeZoneConfigured`/`GroupTimeZoneChanged` audits. | Must | Use one fixed aggregate identity; do not store a fixed offset. |
| FR2 | Replace UTC schedule form fields with familiar local date/time fields and resolve them in Application against the expected settings version. Reject DST gaps and require an explicit offset choice for every ambiguous start or end. | Must | Never silently shift a wall time or choose an overlap occurrence. |
| FR3 | Continue storing only `Shift.StartsAtUtc` and `Shift.EndsAtUtc` as authoritative instants. Zone changes do not rewrite them and require an explicit consequence confirmation. | Must | Existing shifts need no instant conversion. |
| FR4 | Add nullable `Shift.VolunteerInstructions` with the existing 1,000-character bounded-text behavior; retain `Shift.Notes` as coordinator-only and relabel it `Internal coordinator notes`. | Must | One EF Core migration adds group settings and volunteer instructions. |
| FR5 | Provide one shared commitment projection carrying UTC start/end, group IANA zone, title, location, slot role, and volunteer instructions to public browse/request/status/action and coordinator schedule/request/assignment/coverage surfaces. | Must | Prevent divergent page-specific context. |
| FR6 | Render group-local start/end and duration with stable server-side text, explicit zone abbreviation and numeric UTC offset, and `<time datetime>` values containing the authoritative UTC instants. | Must | Do not depend on browser locale or JavaScript. |
| FR7 | Before settings exist, short-circuit public commitment GET and POST paths to a uniform textual unavailable result and no mutation; coordinator schedule pages lead to `/Coordinator/Settings`. | Must | Valid and invalid private tokens remain indistinguishable. |
| FR8 | Add deterministic domain/application/Web and PostgreSQL integration coverage for IANA validation, settings concurrency/audit, DST gaps and overlaps, cross-date duration, privacy, every commitment surface, bootstrap behavior, and zone correction. | Must | Tests use fixed zones and instants, never the host local zone. |

---

## Out of Scope

- Recurring-series generation or recurrence rules
- Browser-local or per-volunteer time zones
- Calendar feeds or calendar-file export
- Geocoding, maps, or structured arrival instructions
- Rewriting existing UTC instants when the group time zone changes

---

## Versioning

The `enhancement` label requires one minor version increment from the implementation branch's current root `VERSION`.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #15 | 2026-09-03 | Initial feature spec |
