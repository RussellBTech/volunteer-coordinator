# Requirements: Establish Production Readiness and Anonymous Abuse Controls

**Issue**: #14
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As a** group maintainer
**I want** deployment health, schema changes, recovery, and public endpoints to fail safely
**So that** a small volunteer organization can operate the service without hidden infrastructure risk

---

## Background

Railway currently probes process liveness even though the application already exposes a PostgreSQL-aware readiness endpoint. Schema migration depends on an opt-in web-startup flag, no backup/restore runbook or recovery objective exists, and accountless request, status, and action endpoints have no explicit abuse control. Production continuity also depends on OIDC and a manually maintained coordinator allowlist.

The approved operational policy separates liveness from deployment admission, runs migrations through an explicit migrate-only process, applies configurable per-IP endpoint tiers, establishes daily backups with a 24-hour recovery-point objective, and documents a two-coordinator handoff without refusing emergency single-coordinator startup. Railway Infrastructure as Code manages the readiness setting. Railway's current IaC DSL cannot manage pre-deploy commands, so that one setting remains an explicitly verified dashboard prerequisite.

---

## Acceptance Criteria

### AC1: Database-aware deployment admission

**Given** PostgreSQL is unavailable
**When** Railway evaluates a new deployment through the repository-managed service health check
**Then** `/health/ready` returns unhealthy and Railway does not admit the deployment, while `/health` remains a separate process-liveness endpoint

### AC2: Controlled schema migration

**Given** a production release contains an EF Core migration
**When** Railway invokes `dotnet VolunteerCoordinator.Web.dll --migrate-only` as its configured pre-deploy command
**Then** one non-serving process applies pending migrations and exits successfully before the new web process can be admitted, and normal web startup never applies migrations

### AC3: Recoverable database

**Given** daily production backups satisfy a 24-hour recovery-point objective
**When** an operator restores a selected backup into an isolated PostgreSQL database by following the runbook
**Then** migrations and the schedule, volunteer, request, assignment, action-token metadata, notification-attempt, and audit state are restored and verified without connecting the application to production

### AC4: Anonymous abuse protection

**Given** one client address exceeds the configured tier for anonymous request mutation, private-token reads, or assignment-action mutation
**When** another request reaches that tier
**Then** the application returns the same generic HTTP 429 response for valid and invalid identifiers, supplies retry guidance, and performs no workflow mutation or token-validity disclosure

### AC5: Coordinator continuity

**Given** two distinct verified OIDC identities are present in the production allowlist
**When** each coordinator signs in independently
**Then** both can operate the existing schedule without shared credentials, and the bootstrap/handoff procedure explains how to add, verify, transfer, and remove coordinator access

---

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Replace the deprecated `railway.toml` source with Railway TypeScript IaC imported from the linked project and configure the Web service health check as `/health/ready`. | Must | Preserve imported service names, resources, and sealed values; review a non-destructive Railway plan before apply. |
| FR2 | Add `--migrate-only` as the sole application-owned production migration mode; it applies EF Core migrations and exits without binding an HTTP port. Remove `Database:MigrateOnStartup` and update Compose to run the same mode as a one-shot dependency before Web starts. | Must | A migration failure exits non-zero and prevents Web startup/deployment admission. |
| FR3 | Configure Railway's Web-service pre-deploy command as `dotnet VolunteerCoordinator.Web.dll --migrate-only` and record its verification in the operations runbook because the current Railway IaC DSL cannot represent it. | Must | No migration from a public request or ordinary Web startup. |
| FR4 | Add ASP.NET Core built-in, per-client-IP fixed-window policies with independent configurable tiers: request POSTs default to 5/minute, request-status and assignment-action GETs default to 30/minute, and assignment-action POSTs default to 10/minute. | Must | Invalid or non-positive configuration fails startup; no external rate-limit service is introduced. |
| FR5 | Resolve the client partition from the proxy-processed remote IP, isolate buckets by policy tier, return a generic plain-text 429 with `Retry-After`, and execute the limiter before antiforgery and page handlers. | Must | Coordinator, health, static-file, and ordinary opening-list traffic are outside these tiers. |
| FR6 | Add an operator runbook covering Railway IaC plan/apply, the dashboard-only pre-deploy prerequisite, variables/secrets, daily backup retention sufficient for a 24-hour RPO, isolated restore and integrity verification, rollback, and two-coordinator OIDC/allowlist handoff. | Must | Never copy production secrets or raw volunteer tokens into source or runbook output. |
| FR7 | Add behavior-focused integration coverage for readiness failure, migrate-only execution, Compose sequencing where practical, independent rate-limit tiers, generic throttling, no-mutation on rejection, and two distinct allowlisted coordinators. | Must | Persistence assertions use isolated PostgreSQL. |

---

## Out of Scope

- Volunteer accounts, CAPTCHA, proof-of-work, or a distributed rate-limit store
- DDoS protection beyond application-level endpoint limits and Railway's platform controls
- Automatic coordinator provisioning from an identity-provider group
- Point-in-time recovery beyond the approved daily-backup 24-hour RPO baseline
- Email provider integration or new scheduling behavior

---

## Versioning

The `enhancement` label requires one minor version increment from the implementation branch's current root `VERSION`. This avoids assuming whether another approved enhancement has already advanced `0.3.0`.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #14 | 2026-09-03 | Initial feature spec |
