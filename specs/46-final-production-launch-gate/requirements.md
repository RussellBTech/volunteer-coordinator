# Requirements: Final Production Launch Gate

**Issue**: #46
**Date**: 2026-09-21
**Status**: Approved
**Author**: RussellBTech

---

## User Story

**As a** project owner
**I want** one operational release gate for the final production launch
**So that** the delivered #13-#22 workflows are proven safely against the real Railway, PostgreSQL, OIDC, and Resend operating environment before any public host is exposed

## Authority and Prerequisites

Issues #13 through #22 are closed. Their local implementation and contract evidence intentionally deferred owner-controlled external launch criteria to this issue. This is an operations/release-gate scope, not an enhancement scope. The gate is not executed by drafting or specifying this issue.

The current root `VERSION` is `0.13.0`. It remains the exact release version for this issue and for the final launch candidate. No public host is retained or exposed until this issue's approved gate is executed successfully.

## Background

The delivered application has local readiness and explicit `--migrate-only` behavior, accountless action/recovery, transactional Resend behavior, recurring scheduling, coordinator exception handling, privacy lifecycle, stewardship views, and local verification. Production ownership still needs evidence against the actual linked Railway project, production PostgreSQL recovery, production OIDC, and the real Resend sandbox. The final release also needs an untrained end-to-end smoke and final privacy, security, and query-budget gates.

The launch gate must be non-destructive, secret-safe, auditable, and reversible. A failed criterion blocks public launch; no criterion is waived by this issue.

## Acceptance Criteria

### AC1: Closure, version, and public-host gate

**Given** issues #13-#22 are closed and the repository root `VERSION` is `0.13.0`
**When** the final launch gate is prepared
**Then** the owner records the closed-issue set, keeps `VERSION` exactly `0.13.0`, makes no product implementation or enhancement change, and does not retain or expose a public host before all remaining criteria in this specification pass

### AC2: Non-destructive Railway IaC reconciliation

**Given** the actual linked Railway project contains the resources and variables intended for this deployment
**When** the owner imports and reconciles that project into the approved TypeScript IaC representation
**Then** the IaC state matches the live project without destructive drift, unreviewed resource replacement, secret disclosure, or deletion; any intentional difference is documented and explicitly approved before apply

### AC3: Readiness admission and exact migration predeploy

**Given** a production candidate is ready for deployment
**When** deployment admission is evaluated
**Then** the platform requires `/health/ready` to report database-aware readiness, runs exactly `dotnet VolunteerCoordinator.Web.dll --migrate-only` once before the serving Web process, never runs schema migration from ordinary Web startup or a public request, and blocks serving traffic on migration or readiness failure

### AC4: Public deployment and rollback

**Given** IaC reconciliation, readiness admission, and migration predeploy have passed
**When** the owner performs the production deployment
**Then** the public host is exposed only after the candidate is admitted, the deployed commit and configuration are recorded without secrets, a rollback to the last known-good deployment is exercised, and rollback restores serving health without destructive database or workflow-state shortcuts

### AC5: Backup, restore, and recovery-point evidence

**Given** production PostgreSQL contains the authoritative schedule and workflow state
**When** the owner verifies operations recovery
**Then** automated backups run daily or more frequently with an observed recovery point no older than 24 hours, an isolated restore completes, integrity checks pass, representative read verification succeeds, restored data is not attached to the public service, and the evidence records retention, isolation, timestamps, and observed result

### AC6: Production OIDC and independent coordinator handoff

**Given** production OIDC configuration and the application allowlist are supplied only through protected runtime configuration
**When** the owner performs the production identity handoff
**Then** two independent coordinator identities authenticate separately and can perform their authorized coordinator workflows, an outgoing coordinator can be removed without shared credentials or schedule loss, an unallowlisted identity is denied, and no OIDC secret or raw session/capability material appears in repository, issue, logs, or evidence

### AC7: Real Resend sandbox matrix

**Given** the verified Resend sandbox sender, reserved test recipient, protected credentials, and production-shaped configuration are available
**When** the owner exercises every delivered #18/#19 notification and recovery path
**Then** the evidence records the verified sender and test recipient, every template, provider message IDs, signed delivered/bounced/complained webhook handling, real timeout/429/5xx/permanent outcomes, absolute retry scheduling and idempotency behavior, recovery and redemption, ordinary reissue and revoke-now reissue, and successful remediation/recovery without exposing credentials, recipient secrets, raw links, or raw webhook bodies

### AC8: No-training desktop and mobile workflow smoke

**Given** the production candidate has passed deployment admission and test data is controlled
**When** an untrained operator and test volunteer exercise the delivered #13-#22 workflows on desktop and narrow mobile surfaces
**Then** the smoke covers schedule setup/publication, local-time and recurring schedules, approval/direct-claim and recurring commitments, request/assignment/action-hub recovery, coordinator exceptions, notifications/resend, privacy/removal, stewardship queues/audit, two-coordinator handoff, and failure/retry/revocation paths; each workflow is understandable without separate technical training and no raw identifiers, provider terminology, secrets, or raw links leak to users

### AC9: Final privacy, security, and query-budget gates

**Given** the final production candidate and its operational evidence are available
**When** the owner runs the final release review
**Then** privacy/retention and removal behavior, secret/raw-link/log/config scans, webhook and token security boundaries, authorization/antiforgery/rate-limit controls, dependency/image/config checks, and representative coordinator query-budget checks all pass with named commands or observable evidence; failures block launch and are not waived by this issue

## Functional Requirements

| ID | Requirement | Priority | Notes |
|----|-------------|----------|-------|
| FR1 | Maintain one release checklist and non-secret evidence ledger for all ACs and the closed #13-#22 prerequisite set. | Must | Record actor, candidate commit/configuration identity, timestamp, command or scenario, observed result, and redacted evidence reference. |
| FR2 | Import and reconcile the actual linked Railway project into the approved TypeScript IaC representation. | Must | Preview and review the plan; no destructive drift, unreviewed replacement, deletion, or secret values. |
| FR3 | Enforce production `/health/ready` admission and the exact one-shot `dotnet VolunteerCoordinator.Web.dll --migrate-only` predeploy. | Must | Serving startup remains non-migrating; public requests never migrate. |
| FR4 | Expose the public host only after the gate admits the candidate and exercise an operational rollback. | Must | Preserve a known-good rollback target and workflow state. |
| FR5 | Operate daily PostgreSQL backups with an observed recovery point of `<=24h` and verify isolated restore, integrity, and representative reads. | Must | Restored data stays isolated from public serving. |
| FR6 | Configure production OIDC and verify two independent coordinator identities, allowlist denial, and documented removal handoff. | Must | No shared credentials; secrets are protected runtime configuration. |
| FR7 | Complete the real Resend sandbox matrix deferred from #18 and #19. | Must | Verified sender/test recipient, all templates, provider IDs, signed webhooks, real failure classes, absolute retry/idempotency, recovery/redemption, both reissue modes, and scans. |
| FR8 | Complete final no-training desktop/mobile smoke across every delivered #13-#22 workflow. | Must | Include normal, failure, recovery, cancellation, revocation, notification, privacy, and handoff paths. |
| FR9 | Run final privacy, security, and representative query-budget checks with reproducible outcomes. | Must | Any failure blocks launch. |
| FR10 | Preserve release version `0.13.0` and forbid new product behavior in this scope. | Must | This is an operational gate; it does not authorize a version bump. |

## Scope Boundaries

### In scope

- Final operational/release-gate checklist and evidence for #13-#22.
- Import/reconcile and non-destructive plan verification for the actual linked Railway project in TypeScript IaC.
- Production `/health/ready` admission and the exact one-shot `--migrate-only` predeploy sequence.
- Public deployment, recorded release state, and exercised rollback.
- Daily PostgreSQL backup with `<=24h` observed RPO, isolated restore, integrity verification, and representative read verification.
- Production OIDC configuration and two-independent-coordinator handoff.
- Real Resend sandbox matrix deferred from #18 and #19, including all sender/template/provider/webhook/failure/retry/recovery/reissue/security evidence named above.
- Final untrained desktop/mobile smoke and privacy, security, and query-budget gates.

### Out of scope

- New product behavior, workflow semantics, UI capability, provider feature, schema feature, or infrastructure redesign.
- Any version bump; `VERSION` remains exactly `0.13.0`.
- Public host exposure, Railway mutation, Resend activity, OIDC changes, production deployment, or production data access before this issue's approved specification and gate execution.
- Marketing email, additional environments, analytics, or unrelated cleanup.
- Weakening, bypassing, or waiving a failed admission, backup, security, privacy, or query-budget gate.

## Versioning

`VERSION` remains exactly `0.13.0`. This operations/release-gate issue authorizes no version bump and carries no `enhancement` or `bug` release semantics.

## Steering Alignment

- **Product** (`steering/product.md`): protects coordinator-owned authoritative state, accountless volunteer actions, notification independence, clear user-visible state, and no-training operation without adding product behavior.
- **Technology** (`steering/tech.md`): retains the .NET 10 modular monolith, PostgreSQL authority, Railway hosting, OIDC allowlist, Resend boundary, `/health/ready`, exact explicit migration process, secret handling, backup/recovery, and verification gates.
- **Structure** (`steering/structure.md`): keeps operational configuration and deployment composition at existing boundaries, uses the canonical issue-owned spec directory, and introduces no alternate source/test layout.

## Deferred-criterion Sources

- #14 deferred public deployment admission, Railway IaC/operations, backups/RPO, production OIDC, and public launch.
- #18 and #19 deferred the real Resend sandbox, provider-event, failure, recovery, and live reissue matrix.
- #22 requires the two-coordinator handoff and bounded query evidence.

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #46 | 2026-09-21 | Owner-approved final operational production-launch gate after #13-#22 closure |
