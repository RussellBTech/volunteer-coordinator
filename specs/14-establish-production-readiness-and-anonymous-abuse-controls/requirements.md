# Requirements: Establish Local Runtime Safety and Anonymous Abuse Controls

**Issue**: #14
**Date**: 2026-09-20
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As a** group maintainer
**I want** local runtime health, schema changes, and anonymous endpoints to fail safely
**So that** I can verify the service without hidden local infrastructure or abuse-control risk

---

## Scope Correction

This issue is intentionally local-only. It covers runtime safety and anonymous abuse controls that can be exercised with the repository's local process, Docker Compose, and local authentication. It does not make or require a public host, mutate a Railway project, or perform production operations.

Production deployment admission, Railway IaC/import/plan/apply, production backup and recovery-point-objective work, the production operations runbook, and live OIDC handoff are deferred to one final launch issue created only after every product issue through #22 is delivered. That launch issue is not created by #14.

---

## Background

The application must expose database-aware local readiness alongside process liveness, run schema migration through an explicit local process separate from ordinary Web startup, and start the local Docker Compose Web service only after PostgreSQL and the one-shot migration service are ready. Accountless request, status, and action endpoints also need explicit per-IP abuse controls. Local coordinator authorization needs a regression proving that two independently allowlisted identities can operate while an unallowlisted identity remains denied.

---

## Acceptance Criteria

### AC1: Local database-aware health

**Given** the local Web process exposes `/health` and `/health/ready`
**When** local PostgreSQL is unavailable
**Then** `/health` continues to report process liveness and `/health/ready` reports the database-dependent service as unhealthy

### AC2: Controlled local schema migration

**Given** a local release contains a pending EF Core migration
**When** the documented `--migrate-only` process runs
**Then** it applies pending migrations and exits without binding an HTTP port, while ordinary Web startup never applies migrations

### AC3: Local Compose sequencing

**Given** local Docker Compose starts PostgreSQL, migration, and Web services
**When** the stack starts
**Then** Web starts only after PostgreSQL is healthy and the one-shot migration service completes successfully; a migration failure prevents Web startup

### AC4: Anonymous abuse protection

**Given** one client IP exceeds the configured tier for anonymous request mutation, private-token reads, or assignment-action mutation
**When** another request reaches that tier
**Then** the excess request receives a generic HTTP 429 response with retry guidance, valid and invalid identifiers receive the same throttling behavior, and no workflow or token state changes

### AC5: Local two-identity authorization regression

**Given** two distinct identities are locally allowlisted
**When** each signs in independently and a third identity is not allowlisted
**Then** both allowlisted identities can access coordinator behavior and the third identity remains unauthorized, without requiring production OIDC

### AC6: Local verification

**Given** the local runtime, Compose, health, migration, authorization, and limiter changes are implemented
**When** the focused local verification runs
**Then** it covers readiness/liveness, migrate-only and ordinary-startup behavior, Compose sequencing, independent per-IP limiter tiers, generic throttling with no mutation, and the two-allowlisted-identity regression

---

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Preserve and verify separate local `/health` liveness and PostgreSQL-aware `/health/ready` readiness behavior. | Must | No public deployment admission or host configuration. |
| FR2 | Make `--migrate-only` the explicit local migration mode and keep ordinary Web startup non-migrating. | Must | Migration failure is non-zero and blocks dependent local Web startup. |
| FR3 | Sequence local Docker Compose PostgreSQL, one-shot migration, and Web services deterministically. | Must | Use the same migrate-only process; no public request triggers migration. |
| FR4 | Enforce configurable per-IP fixed-window anonymous limiter tiers for request mutation, private-token reads, and assignment-action mutation. | Must | Keep tiers independent, generic, and local; no distributed limiter service. |
| FR5 | Preserve the local authorization regression for two independently allowlisted identities and deny an unallowlisted identity. | Must | Development/local authentication only; no live OIDC handoff. |
| FR6 | Perform local verification of all in-scope runtime, Compose, health, migration, limiter, and authorization behavior. | Must | Use the repository's existing unit/integration and local smoke conventions. |

---

## Explicitly Out of Scope

- Public host configuration or public deployment admission
- Railway project mutation, Railway IaC import, plan, apply, or drift verification
- Production backup configuration, restore exercise, or 24-hour RPO evidence
- Production operations runbook
- Live production OIDC configuration, handoff, or identity verification
- Production deployment or production data access of any kind
- New volunteer scheduling behavior, volunteer accounts, email-provider integration, or multi-tenant administration

---

## Versioning

This scope-correction specification authorizes no production code or version change. Any later implementation remains subject to the repository's existing enhancement versioning policy.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #14 | 2026-09-03 | Initial production-readiness feature spec |
| #14 | 2026-09-20 | Scope corrected to local runtime safety and anonymous abuse controls; production launch work deferred until after #22 |
