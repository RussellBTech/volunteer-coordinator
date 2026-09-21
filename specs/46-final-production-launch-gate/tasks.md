# Tasks: Final Production Launch Gate

**Issue**: #46
**Date**: 2026-09-21
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Preconditions and evidence | 2 | [ ] |
| Railway and admission | 3 | [ ] |
| Recovery and identity | 2 | [ ] |
| Resend external matrix | 3 | [ ] |
| Smoke and final gates | 2 | [ ] |
| Deployment and rollback | 1 | [ ] |
| **Total** | **13** | |

## Execution Boundary

These tasks are future launch operations. Do not execute them while drafting, specifying, or merging this spec. Execution begins only after the owner approves the merged spec and explicitly starts issue #46.

No task may add product behavior, change workflow semantics, create a new source/test layout, or change `VERSION`. The root `VERSION` remains exactly `0.13.0`. Do not stash, reset, or overwrite unrelated pending `.gitignore` or steering migration work.

All tasks use controlled data and protected credentials. Evidence is non-secret: never commit or paste OIDC, Railway, Resend, database, recipient, provider, raw webhook, raw-link, session, or connection-string values.

## Phase 1: Preconditions and Evidence

### T001: Freeze the candidate and release ledger

**File(s)/surface**: GitHub issue #46; owner-controlled non-secret launch checklist; candidate commit/image metadata
**Type**: Verify / Record
**Depends**: None
**Acceptance**:

- [ ] GitHub shows #13, #14, #15, #16, #17, #18, #19, #20, #21, and #22 closed.
- [ ] Candidate root `VERSION` is exactly `0.13.0`; no version change is staged or introduced.
- [ ] The ledger names one immutable source commit, image digest, IaC revision, and non-secret configuration revision.
- [ ] The actual linked Railway project is identified and public-host inventory proves no public host is retained before deployment admission.
- [ ] Every future evidence row has an owner, UTC timestamp, exact command/scenario, result, and redacted artifact reference.

### T002: Establish evidence and secret-handling controls

**File(s)/surface**: owner-controlled launch evidence location; repository/config/log/persistence scan boundary
**Type**: Verify / Configure
**Depends**: T001
**Acceptance**:

- [ ] Evidence storage is access-controlled and records identifiers/outcomes only.
- [ ] A scan procedure covers source, IaC, plans, configuration, logs, audit, persistence, container/image layers, screenshots, and issue/PR text.
- [ ] Raw secrets, recipient values, capability/recovery/session links, webhook bodies, rendered messages, and connection strings are excluded from evidence.
- [ ] A remediation/rotation rule exists for any accidental exposure, and no launch row can be marked passed without a clean rescan.

## Phase 2: Railway and Admission

### T003: Import and reconcile the linked Railway project

**File(s)/surface**: approved TypeScript IaC workspace for the actual linked Railway project; Railway read-only inventory and plan
**Type**: Configure / Verify
**Depends**: T001, T002
**Acceptance**:

- [ ] Live project, environment, service, database, build/deploy settings, healthcheck, domains, and variable names are inventoried without secret values.
- [ ] Stable live resources are imported into TypeScript IaC rather than recreated as placeholders.
- [ ] The preview plan has no unreviewed delete, replace, database recreation, public-domain creation, variable deletion, or destructive drift.
- [ ] Every intentional non-destructive difference has an owner approval before apply.
- [ ] Import and plan do not publish a public host or leak protected values.

### T004: Configure readiness admission and exact migration predeploy

**File(s)/surface**: Railway deploy/admission configuration; published Web image and runtime variables; `/health/ready`
**Type**: Configure / Verify
**Depends**: T003
**Acceptance**:

- [ ] Admission requires database-aware `/health/ready`, not only `/health` liveness.
- [ ] The candidate runs exactly `dotnet VolunteerCoordinator.Web.dll --migrate-only` once before the serving Web process, with protected production PostgreSQL configuration.
- [ ] Migrate-only applies pending EF Core migrations, exits successfully, and binds no HTTP port; a failure is non-zero and blocks serving traffic.
- [ ] Ordinary Web startup runs without `--migrate-only` and never applies migrations; no public request can migrate.
- [ ] Readiness failure, timeout, wrong candidate, or wrong database configuration fails closed.

### T005: Review the pre-launch deployment plan

**File(s)/surface**: Railway TypeScript IaC plan; candidate release record
**Type**: Review / Verify
**Depends**: T003, T004
**Acceptance**:

- [ ] IaC plan, candidate commit/image, migration result, readiness configuration, and rollback target refer to the same release candidate.
- [ ] A reviewer records the no-destructive-drift decision and explicit authorization to deploy.
- [ ] Public host exposure is still absent before the deployment step.

## Phase 3: Recovery and Identity

### T006: Verify backup RPO and isolated restore

**File(s)/surface**: production PostgreSQL backup configuration; isolated restore target; non-secret recovery report
**Type**: Configure / Verify
**Depends**: T001, T002, T005
**Acceptance**:

- [ ] Automated backups run daily or more frequently and the observed recovery point is `<=24h` at review time.
- [ ] A selected backup restores into an isolated PostgreSQL target not attached to public serving.
- [ ] Integrity/schema/migration checks pass.
- [ ] Representative schedule, request, assignment, audit, notification, and privacy-state reads pass without contradictory/orphaned sampled workflow rows.
- [ ] Backup retention, restore isolation, timestamps, checks, and outcome are recorded without backup contents or secrets.

### T007: Configure production OIDC and complete independent handoff

**File(s)/surface**: Railway protected OIDC variables and allowlist; separate browser sessions; coordinator audit trail
**Type**: Configure / Verify
**Depends**: T005
**Acceptance**:

- [ ] Authority, client, callback, and allowlist configuration are supplied only through protected runtime variables.
- [ ] Two independent coordinators authenticate in separate sessions, pass the allowlist, and complete authorized coordinator workflows with distinct audit identities.
- [ ] An unallowlisted identity is denied.
- [ ] The outgoing coordinator is removed through the documented protected handoff; remaining and replacement identities continue without shared credentials or schedule loss.
- [ ] Development authentication, copied cookies, raw session links, and raw capability links are not used or recorded.

## Phase 4: Resend External Matrix

### T008: Verify sandbox sender, recipient, templates, and provider identity

**File(s)/surface**: protected Resend sandbox; verified sender; reserved test recipient; delivered #18/#19 notification paths
**Type**: Configure / Verify
**Depends**: T002, T005, T007
**Acceptance**:

- [ ] Verified sender and reserved test recipient are confirmed in the protected provider account and represented only by redacted evidence references.
- [ ] Request receipt, assignment/hub access, recovery/reissue, request decision, volunteer-visible correction, coordinator/volunteer cancellation, and deactivation templates all send through the real sandbox.
- [ ] Every template has safe plain-text/HTML context and records template name/version, provider message ID, application intent/attempt identity, and safe visible state.
- [ ] No recipient copy, rendered body, credential, raw link, or unsafe provider response is persisted or logged.

### T009: Verify signed events, failures, retry, and idempotency

**File(s)/surface**: signed Resend sandbox webhooks; provider failure controls; notification state and audit projections
**Type**: Verify
**Depends**: T008
**Acceptance**:

- [ ] Real signed delivered, bounced, and complained events pass raw-body authentication, deduplication, monotonic/out-of-order handling, and provider-message correlation.
- [ ] Real or provider-supported timeout, HTTP 429, HTTP 5xx, and permanent outcomes map to safe categories without rolling back workflow state.
- [ ] Absolute retry evidence covers 0, 1, 5, 30, and 120 minute offsets where applicable, bounded attempts, lease/crash recovery, and final failure.
- [ ] Identical in-memory payloads reuse an idempotency key only within the approved 24-hour provider window; fresh materialized attempts receive new keys.
- [ ] No raw webhook body, recipient, credential, or provider response body appears in storage, logs, or evidence.

### T010: Verify recovery, redemption, reissue, and scans

**File(s)/surface**: real sandbox recipient; accountless hub/recovery; coordinator ordinary/revoke-now reissue; repository/runtime scans
**Type**: Verify
**Depends**: T009
**Acceptance**:

- [ ] A test volunteer completes recovery and redeems the replacement capability; current hub state and single-use mutation behavior are correct.
- [ ] Ordinary replacement access and revoke-now replacement access both work, follow approved invalidation policy, and produce coordinator audit records.
- [ ] Failed delivery/recovery is visible and recoverable without copying raw links into evidence.
- [ ] Secret, raw-link, recipient, provider-body, webhook-body, configuration, log, audit, and persistence scans pass.

## Phase 5: Smoke and Final Gates

### T011: Run no-training desktop and mobile smoke

**File(s)/surface**: actual Web production candidate; controlled test records; desktop and narrow mobile sessions
**Type**: Verify
**Depends**: T006, T007, T010
**Acceptance**:

- [ ] An untrained operator and test volunteer complete setup/publication, local-time display, recurring/DST review, approval/direct claim, recurring commitment, exception, action-hub recovery/redemption, notification/resend, privacy/removal, stewardship, audit, and handoff journeys.
- [ ] Normal, failure, conflict, retry, bounce/complaint, cancellation, revocation, and recovery paths are exercised.
- [ ] Desktop and narrow mobile surfaces retain order, labels, entered state after errors, and clear next actions without technical training.
- [ ] Routine surfaces expose no raw identifiers, provider terminology, secrets, JSON, UTC-only explanations, or raw links.

### T012: Run final privacy, security, and query-budget gates

**File(s)/surface**: repository, build/image, IaC, runtime, PostgreSQL, logs/audit, and coordinator projections
**Type**: Verify
**Depends**: T011
**Acceptance**:

- [ ] Privacy/retention/anonymization/removal and protected-backup checks pass without membership/attendance claims.
- [ ] OIDC/allowlist, antiforgery, token hashing/expiry/single use, webhook signature, rate-limit, authorization, HTTPS-origin, dependency, image, configuration, and migration checks pass.
- [ ] Source, configuration, logs, audit, persistence, image layers, and evidence contain no secret/raw-link/session/destination/webhook-body/provider-response material.
- [ ] Representative coordinator home, queue, coverage, volunteer-selection, messages, and audit projections remain within the approved bounded query budget with no per-row growth.
- [ ] Every check has a named command or observable result; any failure blocks deployment.

## Phase 6: Deployment and Rollback

### T013: Deploy publicly and exercise rollback

**File(s)/surface**: reconciled Railway project; admitted candidate; public origin; last-known-good deployment
**Type**: Execute / Verify
**Depends**: T003, T004, T005, T006, T007, T008, T009, T010, T011, T012
**Acceptance**:

- [ ] Owner explicitly authorizes deployment only after T001-T012 are recorded as passed.
- [ ] Public host is exposed only at this step; deployment commit/image, admission, migration, readiness, and non-secret configuration identity are recorded.
- [ ] Rollback to the last known-good deployment is exercised using a supported non-destructive operation.
- [ ] The rolled-back service reaches `/health/ready`, PostgreSQL remains authoritative, and no workflow state is deleted, rewritten, or duplicated.
- [ ] Final report maps every AC to evidence and records the launch/rollback decision; no failed gate is waived.

## Dependency Graph

```text
T001 ──▶ T002 ──▶ T003 ──▶ T004 ──▶ T005 ──┬──▶ T006 ──┐
                                            └──▶ T007 ──┤
T005 ─────────────────────────────────────────▶ T008 ──▶ T009 ──▶ T010 ──┤
T006 ───────────────────────────────────────────────────────────────▶ T011 ──▶ T012 ──▶ T013
T007 ───────────────────────────────────────────────────────────────▶ T011
```

## Blocking Conditions

Stop and keep the host non-public if any import/plan difference is destructive, readiness/migration fails, backup RPO exceeds 24 hours, restore is not isolated or readable, either coordinator handoff identity fails, any Resend row is missing, a scan leaks sensitive material, smoke requires technical training, or query growth exceeds the approved budget.

Remediation may be performed and the failed task rerun. A pass requires new evidence; no task may be waived, weakened, or marked complete from an unobserved assumption.

## Versioning and Scope

`VERSION` remains exactly `0.13.0`. This task graph authorizes no enhancement, product behavior, schema change, provider feature, alternate source/test layout, or version bump.

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #46 | 2026-09-21 | Owner-approved final operational production-launch task graph after #13-#22 closure |
