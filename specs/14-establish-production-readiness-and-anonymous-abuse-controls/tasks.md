# Tasks: Establish Production Readiness and Anonymous Abuse Controls

**Issue**: #14
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Runtime safety | 2 | [ ] |
| Operations | 1 | [ ] |
| Verification | 2 | [ ] |
| **Total** | 5 | |

---

## Phase 1: Runtime Safety

### T001: Separate schema migration from Web startup

**File(s)**: `src/VolunteerCoordinator.Web/Program.cs`; `src/VolunteerCoordinator.Web/appsettings.json`; `compose.yaml`; `CONTRIBUTING.md`
**Type**: Modify
**Depends**: None
**Acceptance**:
- [ ] `dotnet VolunteerCoordinator.Web.dll --migrate-only` applies EF Core migrations and exits without binding an HTTP port
- [ ] migration failure exits non-zero and prevents the dependent Web process from starting
- [ ] ordinary Web startup never applies migrations and `Database:MigrateOnStartup` is removed cleanly
- [ ] Compose and native development run the same one-shot migration before Web
- [ ] no public migration endpoint or alternate persistence mechanism is introduced

### T002: Enforce tiered anonymous request limits

**File(s)**: `src/VolunteerCoordinator.Web/Program.cs`; `src/VolunteerCoordinator.Web/appsettings.json`; `src/VolunteerCoordinator.Web/Security/AnonymousRateLimitOptions.cs`
**Type**: Create / Modify
**Depends**: None
**Acceptance**:
- [ ] configurable positive fixed-window defaults enforce 5 request POSTs, 30 private-token GETs, and 10 assignment-action POSTs per client IP per minute
- [ ] tier buckets are independent and unlisted endpoints use a no-limit partition
- [ ] exhausted valid and invalid identifiers receive the same plain-text 429 response and `Retry-After`
- [ ] rejected requests execute before antiforgery/page handlers and make no workflow or action-token mutation
- [ ] proxy-processed remote IP is used without logging route tokens or personal data

---

## Phase 2: Operations

### T003: Migrate Railway configuration and add runbook

**File(s)**: `.railway/railway.ts`; `.railway/README.md`; `package.json`; `package-lock.json`; `railway.toml`; `OPERATIONS.md`; `CONTRIBUTING.md`
**Type**: Create / Modify / Remove
**Depends**: T001
**Acceptance**:
- [ ] Railway's import/migration flow preserves the linked project's actual services, PostgreSQL resources, source, and sealed variables in one TypeScript IaC file
- [ ] `railway.toml` is removed and the reviewed IaC plan contains no unexpected deletion, secret replacement, database recreation, volume change, or unrelated service mutation
- [ ] the Web service health check is `/health/ready`, while `/health` remains documented liveness
- [ ] the dashboard-only pre-deploy command is exactly `dotnet VolunteerCoordinator.Web.dll --migrate-only` and its IaC limitation is explicit
- [ ] the runbook covers variables, plan/apply/rollback, daily backups with a 24-hour RPO, isolated restore verification, and safe drill cleanup
- [ ] two distinct OIDC identities are allowlisted and verified before an outgoing coordinator is removed; startup remains possible with one coordinator

---

## Phase 3: Verification

### T004: Add runtime safety integration coverage

**File(s)**: `tests/VolunteerCoordinator.IntegrationTests/AuthorizationIntegrationTests.cs`; `tests/VolunteerCoordinator.IntegrationTests/PostgreSqlFixture.cs`; focused process-test files under `tests/VolunteerCoordinator.IntegrationTests/`
**Type**: Create / Modify
**Depends**: T001, T002
**Acceptance**:
- [ ] unavailable PostgreSQL makes `/health/ready` unhealthy while `/health` stays healthy
- [ ] a published Web executable proves migrate-only success, failure, no listener, and no migration during ordinary startup
- [ ] low deterministic test limits prove each tier, independent client partitions, independent tier partitions, generic throttling, retry guidance, and unaffected routes
- [ ] persistence assertions prove rejected request/action traffic changes no request, assignment, token, notification, or audit state
- [ ] two separate allowlisted identities can independently authorize coordinator access, while a third identity remains forbidden

### T005: Exercise production operations and version enhancement

**File(s)**: `OPERATIONS.md`; `VERSION`; implementation verification report
**Type**: Modify / Verify
**Depends**: T003, T004
**Acceptance**:
- [ ] increment the implementation branch's current root version by one minor component
- [ ] `npm ci` evaluates the pinned Railway IaC dependency and `railway config plan`/apply/re-plan proves only the approved production changes
- [ ] a deployment with unavailable PostgreSQL is not admitted, and the exact dashboard pre-deploy command successfully gates a migration-bearing release
- [ ] a daily backup restores into isolated PostgreSQL, meets the 24-hour RPO, and passes migration-history, table-integrity, and representative coordinator-read checks
- [ ] both pre-authorized coordinators sign in independently without shared credentials
- [ ] `dotnet format --verify-no-changes VolunteerCoordinator.sln`, Release build, the full isolated-PostgreSQL suite, and Docker/Compose smoke verification pass

---

## Dependency Graph

```text
T001 ──┬──▶ T003 ──▶ T005
       └──▶ T004 ──▶ T005
T002 ─────▶ T004
```

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #14 | 2026-09-03 | Initial feature spec |
