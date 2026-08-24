# Requirements: Complete Coverage Coordination Workflow

**Issue**: #1
**Date**: 2026-08-23
**Status**: Approved
**Author**: RussellBTech

## User Story

**As a** volunteer coordinator
**I want** to configure and publish shifts and manage requests, assignments, confirmations, and coverage
**So that** every upcoming service shift has authoritative coverage state

**As a** volunteer
**I want** to browse published openings, request a slot, see request status, and safely act on assignments without an account
**So that** I can participate with minimal friction

## Background

The remote default branch still contains the legacy VSMS solution. This issue owns the clean cutover: remove VSMS.sln, src/VSMS.*, tests/VSMS.*, src/VSMS.Jobs, seed-test-data.sql, and the obsolete UX audit artifact, then replace them with the approved .NET 10 modular monolith, current repository contract, and deployment files. No compatibility shim or legacy data import is required; no production shift workbook or transactional email provider is available. The first deployment may contain zero shifts. Schedule data remains coordinator-entered and notification delivery remains adapter-driven without controlling workflow success.

## Acceptance Criteria

### AC1: Coordinator Shift Setup

**Given** an authenticated allowlisted coordinator and an empty database
**When** the coordinator opens schedule setup
**Then** a clear zero-shift state leads to authenticated create, edit, correct, and deactivate operations without code or database access

### AC2: Publication and Discovery

**Given** active coordinator-entered shifts
**When** the coordinator publishes selected shifts
**Then** volunteers can browse only published open primary and backup slots with textual status in addition to visual styling

### AC3: Volunteer Request and Status

**Given** a published open slot
**When** a volunteer submits valid contact details
**Then** one pending request is persisted, duplicate pending requests are prevented, and a private opaque status link is returned once for accountless status retrieval

### AC4: Coordinator Decisions and Assignments

**Given** pending requests and published shifts
**When** an authenticated allowlisted coordinator approves, rejects, directly assigns, or reassigns a primary or backup slot
**Then** request and assignment state changes atomically and the coordinator action is audited

### AC5: Secure Volunteer Actions

**Given** an active assignment
**When** a volunteer uses an expiring single-use confirm, decline, or cancel link
**Then** the raw token is verified against a stored hash, consumed once, and the valid transition persists; expired, reused, or mismatched links do not change state

### AC6: Coverage Monitoring

**Given** upcoming published shifts
**When** the coordinator opens coverage monitoring
**Then** uncovered and unconfirmed slots are directly discoverable and assignment or reassignment intervention is available

### AC7: Notification Independence

**Given** a successful workflow transition
**When** a notification adapter is unavailable or fails
**Then** workflow and audit state remain correct and visible while notification state records the separate failure

### AC8: Authentication and Authorization

**Given** OIDC and coordinator allowlist configuration
**When** a user authenticates
**Then** only allowlisted identities can access coordinator pages while volunteer browse, request, status, and action paths remain accountless

### AC9: PostgreSQL Persistence and UTC

**Given** any environment exercising persistence
**When** the application or integration tests run
**Then** PostgreSQL is used, instants are stored as UTC, and EF in-memory and SQLite persistence substitutes are absent

### AC10: Deployable Verified Application

**Given** the implementation is complete
**When** formatting, release build, tests, Docker build, and a local PostgreSQL end-to-end scenario run
**Then** all steering gates pass and the application exposes a Railway-suitable health endpoint

### AC11: Clean Repository Cutover

**Given** remote `master` contains the legacy VSMS solution
**When** issue #1 is delivered
**Then** the legacy solution/projects/obsolete seed and UX artifacts are absent, the current lifecycle/steering contract is present, and `VolunteerCoordinator.sln` with the six reserved projects is the only application layout

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Remove the legacy VSMS solution and obsolete artifacts, install the current repository contract, and create VolunteerCoordinator.sln with the four reserved source projects and two reserved test projects targeting net10.0 with the prescribed dependency direction. | Must | One Web composition root and deployment process. |
| FR2 | Model shifts as coordinator-entered UTC intervals with title, optional location/notes, one primary slot, zero to two backup slots, active state, and publication state. | Must | No hardcoded schedules. |
| FR3 | Implement authenticated create, edit, deactivate, and publish operations with validation, optimistic concurrency, and audit records. | Must | Published past shifts cannot be edited into invalid intervals. |
| FR4 | Expose published future unfilled slots to volunteers with text-based state and responsive Razor Pages. | Must | Primary and numbered backup slots. |
| FR5 | Accept volunteer name, email, and optional phone; normalize email; reject duplicate pending requests for the same slot; return a hashed-token-backed private status URL once. | Must | No volunteer account. |
| FR6 | Support request approval/rejection and direct coordinator assignment/reassignment for every slot. | Must | One active assignment per slot and volunteer per shift. |
| FR7 | Support assigned and confirmed states plus decline and cancel transitions that reopen the slot. | Must | All transitions audited. |
| FR8 | Generate cryptographically random confirm, decline, and cancel tokens, persist only SHA-256 hashes, enforce UTC expiry and single use, and allow coordinator-only regeneration. | Must | Raw links displayed once. |
| FR9 | Present coverage monitoring for upcoming published uncovered and unconfirmed slots ordered by urgency. | Must | Direct intervention links. |
| FR10 | Wire cookie + OIDC authentication and an application email allowlist; provide explicitly enabled Development-only login for local execution. | Must | No production bypass. |
| FR11 | Define a transactional notification application port and persistent notification attempt state; no production provider is selected. | Must | Adapter failure never rolls back domain state. |
| FR12 | Use EF Core PostgreSQL with migrations; use isolated PostgreSQL for integration tests. | Must | No SQLite or EF in-memory provider. |
| FR13 | Add Docker multi-stage packaging, local PostgreSQL Compose configuration, Railway configuration, environment documentation, and `/health`. | Must | No committed secrets. |
| FR14 | Add behavior-focused xUnit coverage for domain transitions and PostgreSQL integration coverage for persistence and critical workflows. | Must | Gherkin remains the acceptance contract. |

## Out of Scope

- A production transactional email provider
- A separate worker or distributed architecture
- Persistent volunteer accounts
- Hardcoded production shift values or workbook import
- Production sample data
- Calendar integrations, SMS, recurring schedule generation, or reporting beyond actionable coverage
- Legacy VSMS compatibility shims, database migration, or data import

## Versioning

The `enhancement` label requires a minor version bump. Update root `VERSION` from `0.1.0` to `0.2.0` as part of delivery.

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #1 | 2026-08-23 | Approved complete first-releasable workflow. |
| #1 | 2026-08-23 | Spec revised before delivery to own the legacy clean cutover and satisfy the v3 executable format. |
