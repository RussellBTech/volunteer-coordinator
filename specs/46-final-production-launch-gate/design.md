# Design: Final Production Launch Gate

**Issue**: #46
**Date**: 2026-09-21
**Status**: Approved
**Author**: RussellBTech

---

## Overview

This specification defines a sequenced operational gate for the first public production launch of the delivered #13-#22 workflows. It does not add product behavior, change workflow semantics, alter the application source layout, or execute any launch activity while the issue is being drafted or specified.

The gate has one authoritative checklist and one non-secret evidence ledger. Every criterion is pass/fail. A failed or unobserved criterion stops the sequence. The public host remains absent or non-public until all pre-launch criteria pass and the owner explicitly starts the deployment step.

The application remains the existing .NET 10 modular monolith: ASP.NET Core Razor Pages, PostgreSQL-authoritative workflow state, accountless volunteer capabilities, authenticated/audited coordinators, in-process notification delivery, and Railway hosting. Operational work configures and verifies those existing contracts; it does not create a second runtime or persistence path.

## Operating Preconditions

1. Confirm through GitHub that #13, #14, #15, #16, #17, #18, #19, #20, #21, and #22 are closed.
2. Confirm the candidate root `VERSION` is exactly `0.13.0`; do not edit it.
3. Preserve the repository's pending `.gitignore` and steering migration independently of this gate. Do not stash, reset, or overwrite unrelated owner work.
4. Use only owner-controlled Railway, PostgreSQL, OIDC, and Resend credentials at execution time. Never put their values in the repository, issue, pull request, spec, screenshots, logs, or evidence ledger.
5. Do not retain or expose a public host before the gate's deployment step.

## Gate State Machine

The owner advances one state only after the prior state has a recorded pass:

```text
Preflight
  -> Railway read-only discovery
  -> TypeScript IaC import and non-destructive plan
  -> Candidate admission and exact migration predeploy
  -> Backup/RPO and isolated restore evidence
  -> Production OIDC two-coordinator handoff
  -> Real Resend sandbox matrix
  -> Desktop/mobile no-training smoke
  -> Privacy/security/query-budget gates
  -> Public deploy
  -> Rollback exercise and final evidence review
  -> Launch accepted
```

The deployment state is not reachable if IaC planning, readiness admission, migration, backup/recovery, identity, Resend, smoke, or final gates are incomplete. Rollback remains available after deployment and is exercised against the last known-good release without destructive database shortcuts.

## Evidence Ledger

Each checklist row records:

- criterion and scenario identifier;
- owner/operator and UTC timestamp;
- candidate commit/image digest and non-secret configuration identity;
- exact command, provider operation, URL path, or user journey;
- observed result and pass/fail decision;
- redacted artifact reference, such as a plan, backup timestamp, restore query result, provider message ID, or screenshot reference.

Evidence records only identifiers and outcomes needed to reproduce the decision. It excludes passwords, API keys, OIDC client secrets, recipient addresses unless the owner stores them in an approved private system, raw capability/recovery/session links, raw webhook bodies, rendered message bodies, access tokens, connection strings, and backup contents. If a tool prints a secret or raw link, stop, rotate or invalidate it, remove the exposure from the evidence channel, and record only the remediation outcome.

The final non-secret report may be attached to the issue or stored as the issue-owned `verification-report.md` during execution. It must map every acceptance criterion to a pass and name the command or observable scenario used.

## Railway and TypeScript IaC Reconciliation

### Read-only discovery

Use the actual linked Railway project, not a newly created placeholder. First capture a redacted inventory of project, environment, service, database, build, deploy, healthcheck, domain, variable names, and access-policy identifiers. Record whether a value is present and its source without copying secret values. Do not create a public domain or mutate resources during discovery.

### Import and plan

Import the live resources into the approved TypeScript IaC representation used for this project. The import must preserve stable provider resource identities and model the actual linked project rather than approximating it from repository files. Run a provider plan/preview after import and compare it with the redacted inventory.

The plan must contain no unreviewed delete or replace operation. A rename, resource replacement, domain change, database recreation, variable deletion, or destructive migration is a blocking difference. An intentional non-destructive difference requires an owner decision recorded with the plan before apply. Secret values are supplied through protected runtime configuration and are never rendered into TypeScript, plan output, logs, or pull requests.

Apply is allowed only after the plan is reviewed and the no-destructive-drift condition is recorded as passed. If the actual project cannot be imported safely, stop before apply and retain the service non-public.

### Public-host constraint

The linked project remains without a retained public host until the gate reaches the public-deploy state. Railway resource import, state refresh, or plan must not publish a domain, enable public DNS, or advertise an origin. The first public origin is created only as part of the admitted deployment and is recorded with the release evidence.

## Candidate Admission and Migration

### Candidate identity

Select one immutable application commit and image digest. Record the commit, image digest, IaC plan identity, and non-secret configuration revision in the ledger. Do not mix source, image, IaC, or configuration revisions during a gate run.

### Exact predeploy

Run the same published Web image with the exact command:

```text
dotnet VolunteerCoordinator.Web.dll --migrate-only
```

The process must use the production PostgreSQL connection supplied through protected runtime configuration, apply pending EF Core migrations, exit successfully, and bind no HTTP port. It runs exactly once for the admitted candidate before the serving Web process. A non-zero exit blocks serving traffic and blocks public deployment.

The serving process starts the same image through its ordinary command with no `--migrate-only` argument. Ordinary Web startup does not apply migrations. No public endpoint, health probe, or request can trigger migration.

### Readiness admission

Configure Railway deployment admission to require `/health/ready`, not only process liveness. The probe must verify database-aware readiness after the migration step. `/health` remains liveness; an unavailable PostgreSQL dependency must not be represented as ready. Admission fails closed on timeout, non-success response, or a serving process that is not backed by the expected candidate/database configuration.

## Public Deployment and Rollback

After all pre-launch rows pass, deploy the immutable candidate through the reconciled Railway project. Record the deployment identifier, commit/image digest, admission result, migration result, readiness result, and the time the public origin became available. Do not record secret variables or live volunteer data.

Keep the last known-good deployment as the rollback target. Exercise rollback using the platform's supported deployment rollback operation or an equivalent non-destructive release switch. Verify the rolled-back serving process reaches `/health/ready`, preserves PostgreSQL authority, and does not undo or duplicate committed workflow transitions. Record the rollback result and return to the intended candidate only through an equally recorded deployment operation.

A rollback that requires destructive schema changes, data deletion, secret disclosure, or manual workflow-state rewriting fails this criterion and stops launch acceptance.

## PostgreSQL Backup and Recovery

Configure protected automated backups at least daily. Verify an observed recovery point timestamp no older than 24 hours at the decision time and record retention/rotation metadata without exposing backup contents.

Restore a selected backup into an isolated PostgreSQL target that is not reachable by the public Web service. Verify:

- PostgreSQL integrity and schema consistency;
- migration/schema version expected for the selected backup;
- representative coordinator schedule, request, assignment, audit, notification, and privacy-state reads;
- no unexpected orphaned or contradictory workflow rows for the sampled data;
- restored credentials/configuration are not automatically connected to public serving.

Destroy or retain the isolated restore according to the owner-approved retention policy. The report records isolation, backup and restore timestamps, checks performed, and outcomes. A restore that cannot be isolated, read, or verified blocks launch.

## Production OIDC and Coordinator Handoff

Supply OIDC authority, client identifier/secret, callback configuration, and the normalized coordinator allowlist through protected Railway variables. Validate the authority and callback against the actual production origin without copying secret values into evidence.

Exercise two independent coordinator identities in separate browser sessions. Each must authenticate through OIDC, pass the application allowlist, view and operate authorized coordinator workflows, and produce the expected audit identity. Exercise an unallowlisted identity and verify denial. Remove the outgoing coordinator through the documented protected configuration handoff, then re-verify both the remaining and replacement independent identities without shared credentials or schedule loss.

Do not replace OIDC with development authentication, shared accounts, copied cookies, or raw session/capability links. Rotate or invalidate any temporary handoff material after verification.

## Real Resend Sandbox Matrix

Use only the owner-approved Resend sandbox account, verified sender, reserved test recipient, and protected credentials. Record sender/recipient identifiers in the approved private evidence system or as redacted references; never put addresses or secrets in the repository or public issue evidence.

### Message and provider identity

Exercise every delivered #18/#19 template and path, including request receipt, assignment/hub access, recovery/reissue, request decision, volunteer-visible correction, coordinator/volunteer cancellation, and deactivation. For each, record the template name/version, provider message ID, safe application notification intent/attempt identity, accepted result, and visible non-secret state. Confirm group-local commitment context and safe HTML/text rendering.

### Webhooks and provider failures

Submit or observe signed real sandbox events for delivered, bounced, and complained outcomes. Verify raw-body signature/timestamp authentication, event deduplication, monotonic/out-of-order handling, provider-message correlation, and safe coordinator state. Do not retain raw bodies or recipient content.

Exercise real or provider-supported sandbox representations of timeout, HTTP 429, HTTP 5xx, and permanent failure. Record the safe classification, retry outcome, final state, and absence of workflow rollback. Verify the absolute retry schedule (0, 1, 5, 30, and 120 minutes where applicable), bounded attempts, lease/recovery handling, and reuse of an idempotency key only for the same in-memory payload within its valid window. A fresh materialized attempt receives a new key and never duplicates a workflow transition.

### Recovery and reissue

Use the real sandbox recipient to redeem the accountless recovery capability and verify the current hub/action state. Exercise ordinary replacement access and revoke-now replacement access. Confirm earlier capabilities follow the approved invalidation policy, replacement redemption works once, coordinator actions are audited, and failed delivery/recovery is visible without exposing raw links.

Run secret, recipient, provider-response, raw-link, raw-webhook, configuration, log, audit, and persistence scans after the matrix. A scan finds only safe identifiers/categories and no secret-bearing material.

## No-Training Desktop and Mobile Smoke

Use controlled test records only. A person who did not receive product-specific technical training performs the journeys on an ordinary desktop viewport and a narrow mobile viewport. The smoke covers all delivered workflow families:

- coordinator first setup, schedule creation/edit/correction/publication, local-time display, recurrence/DST review, and coverage monitoring;
- approval-required and direct-claim requests, recurring commitments, withdrawals, occurrence exceptions, reassignment, deactivation, and cancellation;
- volunteer browse/request/status/action-hub confirmation, decline, cancellation, recovery, redemption, expiry/replacement, and notification failure recovery;
- coordinator message state/resend/reissue, audit/history, privacy notice/removal, stewardship queues, reusable volunteer selection, bounded query behavior, and independent handoff;
- normal, concurrency/conflict, retry, bounce/complaint, revoke-now, and rollback-adjacent operational paths.

Record task completion, understandable next action, preserved state after errors, mobile ordering/labels, keyboard/assistive semantics where applicable, and absence of raw identifiers, provider terminology, secrets, JSON, UTC-only explanations, or raw URLs in routine surfaces. This is verification of delivered behavior, not authorization to add a new workflow.

## Final Privacy, Security, and Query-Budget Gates

Run the existing repository and deployment checks appropriate to the candidate, plus the owner-approved production scans. At minimum verify:

- privacy notice, retention/anonymization, removal dependencies, protected-backup handling, and no membership/attendance claim;
- secret, raw capability/recovery link, raw session, destination, webhook-body, provider-response, and connection-string absence from source, config, logs, audit, persistence, container/image layers, and evidence;
- OIDC allowlist, antiforgery, token hashing/expiry/single use, webhook verification, rate-limit, authorization, and HTTPS/public-origin boundaries;
- dependency, image, IaC-plan, configuration, and migration admission checks;
- representative coordinator queue/audit/stewardship queries stay within the approved bounded query budget and do not regress to per-row growth.

Every check records its exact command or observable result. A failed scan, query-budget measurement, privacy check, or security boundary is a blocking result.

## Performance and Query Budget

Use the representative recurring-history and volunteer-volume fixture already used by #22. Measure coordinator home, queues, coverage, volunteer selection, messages, and audit projections. Confirm set-based bounded reads, expected indexes, and no per-row query growth. The evidence records query count/shape and dataset size without volunteer contact data.

## Failure and Stop Rules

Stop before mutation when import or plan is destructive, credentials are incomplete, the public host would be exposed early, migration or readiness fails, backup RPO exceeds 24 hours, restore is not isolated, either coordinator handoff identity fails, any Resend matrix row is missing, a scan leaks sensitive material, a smoke journey requires technical training, or a query-budget gate regresses.

The owner may remediate and rerun a failed row. The owner may not mark it passed without new evidence, waive it, or weaken the application contract. No code workaround, data edit, or new product behavior is introduced under this release gate.

## Steering Alignment

- **Product**: protects authoritative schedule state, low-friction accountless journeys, visible delivery state, safe coordinator actions, privacy, and no-training operation.
- **Technology**: retains .NET 10, PostgreSQL authority, Railway variables, OIDC plus allowlist, Resend boundaries, explicit migrations, readiness admission, backup/recovery, and secret-safe verification.
- **Structure**: keeps deployment/composition and provider configuration at existing boundaries, uses the canonical `specs/{N}-{slug}/` archive, and introduces no alternate source or test layout.

## Verification Strategy

| Gate | Evidence | Blocking condition |
|------|----------|--------------------|
| Closure/version/host | GitHub issue state, root `VERSION`, redacted host inventory | Any prerequisite open, version changed, or host exposed early |
| Railway/IaC | Import record and non-destructive plan | Delete/replace/drift not explicitly approved |
| Admission/migration | Exact command output, no-listener observation, `/health/ready` result | Migration failure, listener during migrate-only, or readiness failure |
| Deployment/rollback | Deployment and rollback identifiers plus health observations | No exercised rollback or unhealthy target |
| Backup/restore | Backup timestamp, isolated restore checks, representative reads | RPO >24h, unisolated restore, integrity/read failure |
| OIDC handoff | Two independent sessions, denial, removal/re-add evidence | Shared credentials, denial failure, or secret exposure |
| Resend | Complete template/provider/failure/webhook/recovery matrix | Missing row, unsafe persistence/log, or incorrect retry/idempotency |
| Smoke/gates | Desktop/mobile journey log, privacy/security scans, query measurements | Training dependency, sensitive leak, failed scan, or query growth |

## Versioning

`VERSION` remains exactly `0.13.0`. This operational design authorizes no version bump.

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #46 | 2026-09-21 | Owner-approved final operational production-launch design after #13-#22 closure |
