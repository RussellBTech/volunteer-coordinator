# Design: Establish Production Readiness and Anonymous Abuse Controls

**Issue**: #14
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Overview

Keep the existing .NET 10 modular monolith and PostgreSQL authority. Web composition owns health, migration-process selection, proxy-aware client identity, rate limiting, and endpoint responses. EF Core remains the only schema migration mechanism. No workflow/domain status changes and no persistence migration are required by the abuse-control work itself.

Production deployment becomes a three-gate sequence: Railway runs the explicit migrate-only command, starts the new Web process, and admits it only after `/health/ready` can reach PostgreSQL. `/health` remains liveness for diagnosis. The deprecated per-service `railway.toml` is migrated to Railway's current project-level TypeScript IaC. Because the current IaC DSL does not expose a pre-deploy-command field, the exact dashboard setting is a required runbook check rather than a fabricated IaC property.

---

## Deployment and Migration Design

### Application process modes

`Program.cs` recognizes the exact `--migrate-only` switch after normal configuration and dependency registration. In that mode it builds the service provider, resolves `VolunteerCoordinatorDbContext`, executes `Database.MigrateAsync`, logs completion, and returns before middleware mapping or `RunAsync`. Exceptions remain fatal so the process exits non-zero. Unknown command-line switches retain normal ASP.NET Core configuration behavior; there is no public migration route.

Delete the `Database:MigrateOnStartup` branch and setting. Ordinary Web startup always serves without schema mutation. `compose.yaml` adds a one-shot migration service using the same built image and command, and Web depends on that service completing successfully plus PostgreSQL being healthy. Native development instructions run the migrate-only command explicitly before `dotnet run`.

### Railway configuration

Use the Railway CLI's migration/import flow against the linked project so `.railway/railway.ts` preserves real project/service names, PostgreSQL resources, GitHub source, variables through `preserve()`, and other existing desired state. Add the current `railway` TypeScript package and lockfile required to evaluate the authoring file. Change only the Web service health check to `/health/ready` with the existing timeout unless the imported live value differs intentionally. Remove `railway.toml`; a service must not be managed by both systems.

Before apply, `railway config plan` must show no unexpected deletion, variable replacement, database recreation, volume change, or unrelated service mutation. Apply the reviewed plan, then re-plan to prove no drift. Separately configure and verify the Web service's dashboard pre-deploy command as `dotnet VolunteerCoordinator.Web.dll --migrate-only`; Railway IaC does not currently represent this property. A failed migration blocks deployment before Web admission.

---

## Anonymous Rate-Limit Design

Add a Web-owned options type bound from `AnonymousRateLimits`. Validate all permit counts and window durations as positive at startup. Safe committed defaults are:

| Tier | Endpoint and method | Permit limit | Window |
|------|---------------------|--------------|--------|
| Request mutation | `POST /Shifts/Request/{slotId}` | 5 | 1 minute |
| Private-token read | `GET /Requests/Status/{token}` and `GET /Actions/{token}` | 30 | 1 minute |
| Assignment-action mutation | `POST /Actions/{token}` | 10 | 1 minute |

Configure ASP.NET Core's built-in partitioned fixed-window limiter. The partition key combines the tier name with `HttpContext.Connection.RemoteIpAddress`; a missing address uses one shared `unknown` key rather than bypassing the limiter. `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` remains the Railway proxy integration, so the partition observes the proxy-processed client address. Each tier uses no queue: the first excess request is rejected immediately.

The global partition selector returns a no-limit partition for every method/path not listed above. This keeps coordinator pages, health probes, static content, and the public openings list unaffected. Place `UseRateLimiter` after forwarded-header/routing processing and before authentication-independent antiforgery/page execution, so rejected POSTs cannot mutate workflow state or consume action tokens.

The rejection callback returns status 429, content type `text/plain`, a generic `Too many requests. Try again later.` body, and `Retry-After` based on the configured window. It never invokes a token lookup and never varies by route identifier validity. Logs may include the policy tier and address but never route tokens, form values, volunteer contact information, or token-validity results.

The limiter is intentionally process-local. Railway must operate one Web replica for this baseline; distributed or multi-region limiting requires a later approved issue.

---

## Recovery and Coordinator Operations

Create `OPERATIONS.md` and link it from `CONTRIBUTING.md`. The runbook contains:

1. Railway CLI link, import/migrate, plan, apply, and drift-check commands, with explicit destructive-plan stop conditions.
2. Web and PostgreSQL variables, OIDC secret handling, `/health` versus `/health/ready`, and the dashboard-only pre-deploy command verification.
3. Daily Railway PostgreSQL backup configuration and retention sufficient to maintain a 24-hour RPO, plus a portable logical-backup fallback where supported.
4. Restore only into a newly created isolated PostgreSQL target; point a non-production Web process at it only after restore.
5. Verify EF migration history and counts/referential integrity for shifts, slots, volunteers, requests, assignments, action-token metadata, notification attempts, and audits; exercise representative coordinator reads without exposing raw data in logs.
6. Record drill timestamp, selected backup timestamp, achieved recovery point, verification result, and cleanup. Never overwrite production as a drill.
7. Configure two separate verified OIDC users in `Coordinator__AllowedEmails__0` and `__1`; test each login independently; remove the outgoing coordinator only after the incoming coordinator succeeds. This is an operational requirement, not a startup/readiness failure, so emergency single-coordinator operation remains possible.

---

## Security Considerations

- Rate limiting supplements, not replaces, antiforgery, token hashing, expiry, single use, authorization, and domain invariants.
- Valid and invalid token paths receive identical throttling responses without database access after a partition is exhausted.
- Client-IP trust relies on the existing Railway forwarded-header integration; direct untrusted forwarding headers must not be enabled outside a trusted proxy deployment.
- Railway IaC preserves secret values rather than materializing them. Plans, docs, test output, backups, and logs must not contain OIDC secrets, connection strings, volunteer contacts, raw status tokens, or raw action tokens.
- Backup access and restore targets require least-privilege operator credentials distinct from application traffic where the provider supports them.

---

## Performance Considerations

Fixed-window state is in process memory and bounded by active client/tier partitions. Idle partitions expire with their windows. Rejected traffic does not perform page-model or PostgreSQL work. The selected single-replica baseline makes enforcement predictable; scale-out requires a shared limiter design in a later issue.

Readiness adds one bounded PostgreSQL check only during probes. Migration work runs once outside the serving process. Backup and restore drills run outside production request handling.

---

## Testing Strategy

| Layer | Type | Coverage |
|-------|------|----------|
| Web integration | In-memory host plus isolated PostgreSQL | Per-tier exhaustion, independent client/tier buckets, generic 429 response, `Retry-After`, no workflow/token mutation, unaffected coordinator and health routes, two allowlisted identities. |
| Process integration | Published Web executable plus isolated PostgreSQL | `--migrate-only` applies pending migrations and exits without listening; failure returns non-zero; normal startup does not migrate. |
| Deployment | Railway CLI plan and endpoint smoke checks | IaC has no destructive drift, `/health/ready` blocks on unavailable PostgreSQL, `/health` remains live, dashboard pre-deploy command is exact. |
| Recovery | Isolated PostgreSQL drill | A daily backup restores all authoritative tables and migration history and meets the 24-hour RPO record. |

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #14 | 2026-09-03 | Initial feature spec |
