# Tasks: Establish Local Runtime Safety and Anonymous Abuse Controls

**Issue**: #14
**Date**: 2026-09-20
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Runtime safety | 2 | [ ] |
| Abuse and authorization | 2 | [ ] |
| Verification | 2 | [ ] |
| **Total** | 6 | |

---

## Phase 1: Runtime Safety

### T001: Preserve local health and process safety

**File(s)**: `src/VolunteerCoordinator.Web/Program.cs`; `src/VolunteerCoordinator.Web/appsettings.json`; focused local process-test files under `tests/VolunteerCoordinator.IntegrationTests/`
**Type**: Modify / Verify
**Depends**: None
**Acceptance**:
- [ ] `/health` remains process liveness and `/health/ready` remains PostgreSQL-aware readiness; unavailable local PostgreSQL makes readiness unhealthy without making liveness a database check
- [ ] `dotnet VolunteerCoordinator.Web.dll --migrate-only` applies EF Core migrations, exits successfully, and never binds an HTTP port
- [ ] migration failure exits non-zero
- [ ] ordinary Web startup never applies migrations and obsolete startup-migration configuration is removed cleanly
- [ ] no public migration endpoint or alternate persistence mechanism is introduced

### T002: Sequence local Compose migration before Web

**File(s)**: `compose.yaml`; `CONTRIBUTING.md`; focused local Compose verification
**Type**: Modify / Verify
**Depends**: T001
**Acceptance**:
- [ ] PostgreSQL health gates the one-shot migration service
- [ ] the migration service runs the same `--migrate-only` process used by native local instructions
- [ ] Web depends on successful migration completion and healthy PostgreSQL
- [ ] a migration failure prevents Web startup
- [ ] local Compose configuration and smoke output prove the ordering without contacting a public host or production service

---

## Phase 2: Abuse and Authorization

### T003: Enforce independent per-IP anonymous tiers

**File(s)**: `src/VolunteerCoordinator.Web/Program.cs`; `src/VolunteerCoordinator.Web/Security/AnonymousRateLimitOptions.cs`; `src/VolunteerCoordinator.Web/appsettings.json`
**Type**: Create / Modify
**Depends**: None
**Acceptance**:
- [ ] configurable positive fixed-window defaults enforce 5 request POSTs, 30 private-token GETs, and 10 assignment-action POSTs per client IP per minute
- [ ] request, private-token-read, and assignment-action tiers use independent buckets, and unlisted endpoints use a no-limit partition
- [ ] proxy-processed remote IP is used; a missing address is limited rather than bypassing the policy
- [ ] exhausted valid and invalid identifiers receive the same plain-text 429 response and `Retry-After`
- [ ] rejected requests execute before antiforgery/page handlers and make no workflow or action-token mutation
- [ ] limiter logs never contain route tokens, form values, volunteer contact information, or token-validity results

### T004: Preserve local two-identity authorization

**File(s)**: `tests/VolunteerCoordinator.IntegrationTests/AuthorizationIntegrationTests.cs`; `tests/VolunteerCoordinator.IntegrationTests/CoordinatorWebFactory.cs`
**Type**: Modify / Verify
**Depends**: None
**Acceptance**:
- [ ] two distinct normalized local identities can authenticate independently and access coordinator behavior
- [ ] a third identity not in the local allowlist remains unauthorized
- [ ] the regression uses local/development authentication and does not require live OIDC or a production identity-provider group
- [ ] coordinator authorization remains separate from anonymous volunteer access

---

## Phase 3: Verification

### T005: Cover local runtime and abuse behavior

**File(s)**: `tests/VolunteerCoordinator.IntegrationTests/`; focused process-test files under `tests/VolunteerCoordinator.IntegrationTests/`
**Type**: Create / Modify
**Depends**: T001, T003, T004
**Acceptance**:
- [ ] unavailable PostgreSQL makes `/health/ready` unhealthy while `/health` stays healthy
- [ ] a published local Web executable proves migrate-only success, migration failure, no listener, and no migration during ordinary startup
- [ ] deterministic low limits prove each tier, independent client partitions, independent tier partitions, generic throttling, retry guidance, and unaffected routes
- [ ] persistence assertions prove rejected request/action traffic changes no request, assignment, token, notification, or audit state
- [ ] two separate allowlisted identities can independently authorize coordinator access while a third identity remains forbidden

### T006: Exercise local verification and Compose smoke

**File(s)**: `compose.yaml`; `tests/VolunteerCoordinator.IntegrationTests/`; `CONTRIBUTING.md`
**Type**: Verify
**Depends**: T002, T005
**Acceptance**:
- [ ] repository-local formatting, Release build, and focused isolated-PostgreSQL tests pass
- [ ] `docker compose config --quiet` passes for the local stack
- [ ] the local Compose smoke proves PostgreSQL health, migration completion, Web startup ordering, and migration-failure blocking
- [ ] local endpoint smoke covers liveness, readiness, anonymous tiers, generic throttling, and two allowlisted identities
- [ ] verification records only local evidence; no public host, Railway, production backup/RPO, production runbook, or live OIDC handoff is attempted

---

## Dependency Graph

```text
T001 ──▶ T002 ──┐
                 ├──▶ T006
T001 ──┬──▶ T005 ┘
T003 ──┤
T004 ──┘
```

---

## Scope Boundary

The task graph contains no Railway IaC/import/plan/apply task, public deployment-admission task, production backup or RPO exercise, production runbook task, or live OIDC handoff. Those activities are deferred to one final launch issue created only after every product issue through #22 is delivered.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #14 | 2026-09-03 | Initial production-readiness task graph |
| #14 | 2026-09-20 | Task graph corrected to local runtime safety and anonymous abuse controls |
