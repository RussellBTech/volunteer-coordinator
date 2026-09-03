# Requirements: Guide Nontechnical Coordinators Through Setup and Routine Work

**Issue**: #17
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As a** first-time or rotating volunteer coordinator
**I want** the application to guide me through setup and routine work in familiar language
**So that** I can maintain coverage confidently without technical training or separate documentation

---

## Background

The coordinator surface currently exposes separate Schedule, Requests, Coverage, and Audit pages but no coordinator home or end-to-end setup journey. Routine work requires scanning pages, assignment requires retyping a known volunteer's contact information, high-impact actions execute directly from compact forms, and the audit view exposes raw identifiers and JSON. Useful validation exists, but terminology and recovery are inconsistent.

This issue establishes reusable interaction rules before recurring scheduling and broader stewardship work. It depends on issue #15's group-local time contract and issue #13's safe deactivation/cancellation commands. The setup checklist explains the existing approval policy; it does not introduce self-scheduling, which remains issue #21. Failed-message attention is limited to current or future commitments until issue #19 adds delivery retry/resend.

---

## Acceptance Criteria

### AC1: Guided first setup

**Given** an allowlisted coordinator signs in to an empty installation without product training
**When** they follow the coordinator home
**Then** one short ordered checklist leads through group time zone, the current request-approval policy, first schedule entry, review, and publication, always showing one primary next action and no dependency on external documentation

### AC2: Plain-language routine navigation

**Given** pending requests, uncovered commitments, unconfirmed volunteers, or failed messages for current/future commitments need attention
**When** the coordinator opens the application
**Then** plain-language counts ordered by urgency link directly to the filtered page and action that resolves each item, while a clear all-caught-up state replaces empty metrics

### AC3: Safe high-impact changes

**Given** a coordinator is about to publish or deactivate a shift, cancel an assignment, or replace a volunteer
**When** they review the action
**Then** a server-rendered preview names affected people, local dates, slots, and consequences, offers a no-mutation way back, and requires an explicit labelled confirmation before the authoritative command runs

### AC4: Recoverable forms and known-volunteer assignment

**Given** a coordinator selects a known volunteer, enters new contact data, or submits invalid/stale form data
**When** the form is processed
**Then** choose-known and add-new are separate labelled paths, valid entered values remain, field errors appear beside fields and in a summary, concurrency errors explain reload/review in plain language, and no internal code or identifier is required

### AC5: Accessible mobile operation

**Given** a coordinator uses a 320 CSS-pixel viewport, keyboard only, or assistive technology
**When** they complete initial setup and routine interventions
**Then** reading/focus order, headings, labels, status text, targets, summaries, previews, confirmations, and back actions remain complete without color, hover, drag-and-drop, or hidden gestures

### AC6: No technical leakage

**Given** a coordinator performs ordinary schedule, request, coverage, message, or history work
**When** pages, errors, warnings, and confirmations are displayed
**Then** raw GUIDs, JSON, UTC-conversion instructions, stack/database/provider terminology, and internal action/status codes are replaced by local-time and plain-language descriptions without hiding security, privacy, or workflow consequences

---

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Add `/Coordinator` as the post-login home with setup and routine modes selected from authoritative PostgreSQL state. | Must | Group-local time from #15; deactivation/cancellation from #13. |
| FR2 | In setup mode, show ordered steps for time zone, the fixed `Volunteers request; coordinators approve` policy, first shift, review, and publication, with exactly one primary next action. | Must | Explains current policy; adds no signup-policy setting. |
| FR3 | In routine mode, show actionable counts for pending requests, uncovered commitments, unconfirmed volunteers, and failed messages tied to commitments whose shifts have not ended. | Must | One batch projection; links preserve a plain-language filter, not raw query IDs. |
| FR4 | Add server-rendered review/confirm steps for publish, deactivation, coordinator cancellation, and replacement. Final POSTs revalidate authoritative state and preserve existing atomic/concurrency behavior. | Must | No JavaScript-only confirmation and no new bulk mutation. |
| FR5 | Split assignment into `Choose known volunteer` and `Add someone new`; known selection uses a stable server-side identifier but displays name and email and never asks the coordinator to retype contact data. | Must | Exclude removed/ineligible records when privacy lifecycle is present. |
| FR6 | Standardize coordinator vocabulary, action hierarchy, form summaries, field errors, concurrency recovery, empty states, and no-mutation back links in shared Web presentation components. | Must | One clear primary action per task. |
| FR7 | Replace routine audit JSON/identifier output and provider-centric warnings with human-readable event/message summaries while retaining full authoritative audit data in PostgreSQL. | Must | Never leak raw tokens or hide actionable failure. |
| FR8 | Add keyboard, semantic, narrow-mobile, and browser-driven coverage for first setup and routine task completion plus behavior tests for counts, previews, no-mutation backs, confirmation revalidation, input preservation, and authorization. | Must | A first-time walkthrough may use only text visible in the application. |

---

## Out of Scope

- Configurable self-scheduling or signup policy; issue #21 owns that behavior
- Notification retry/resend or provider integration; issue #19 owns delivery operations
- New bulk schedule mutation commands
- Video tutorials, a general content-management system, or a separate help center
- Removing necessary security, privacy, concurrency, or consequence information to shorten copy

---

## Versioning

The `enhancement` label requires one minor version increment from the implementation branch's current root `VERSION`.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #17 | 2026-09-03 | Initial feature spec |
