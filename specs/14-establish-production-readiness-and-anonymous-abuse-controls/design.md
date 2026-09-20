# Design: Establish Local Runtime Safety and Anonymous Abuse Controls

**Issue**: #14
**Date**: 2026-09-20
**Status**: Approved
**Author**: RussellBTech

---

## Overview

Keep the existing .NET 10 modular monolith and PostgreSQL authority. Web composition owns local health endpoints, migration-process selection, proxy-aware client identity, rate limiting, and endpoint responses. EF Core remains the only schema migration mechanism. The scope changes no workflow or domain state and introduces no persistence model change for abuse controls.

This is a local-only design. It exercises the process directly, through Docker Compose, and through local authentication. It does not configure a public host, mutate Railway, perform deployment admission, define production recovery operations, or hand off live OIDC access. Those concerns remain deferred to one final launch issue created only after every product issue through #22 is delivered.

---

## Local Health and Migration Design

### Health endpoints

Keep `/health` as process liveness and `/health/ready` as the PostgreSQL-aware readiness endpoint. Local verification must demonstrate that a serving process can report liveness while an unavailable PostgreSQL dependency makes readiness unhealthy. Neither endpoint performs schema mutation.

### Application process modes

`Program.cs` recognizes the exact `--migrate-only` switch after normal configuration and dependency registration. In that mode it builds the service provider, resolves `VolunteerCoordinatorDbContext`, executes `Database.MigrateAsync`, logs completion, and returns before middleware mapping or `RunAsync`. Exceptions remain fatal so the process exits non-zero. The process must not bind an HTTP port in migrate-only mode.

Remove any ordinary-startup migration branch and its obsolete setting. Ordinary Web startup always serves without schema mutation. Unknown command-line switches retain normal ASP.NET Core configuration behavior; there is no public migration route.

### Docker Compose sequencing

The local Compose definition uses PostgreSQL health to gate a one-shot migration service that runs the same built Web image with `--migrate-only`. The Web service depends on successful completion of that migration service as well as healthy PostgreSQL. A migration failure therefore prevents Web startup. The native local instructions use the same migrate-only command explicitly before ordinary `dotnet run`.

No production or Railway configuration is added, removed, imported, planned, applied, or verified by this design.

---

## Anonymous Rate-Limit Design

Add a Web-owned options type bound from `AnonymousRateLimits`. Validate all permit counts and window durations as positive at startup. Keep these local defaults:

| Tier | Endpoint and method | Permit limit | Window |
|------|---------------------|--------------|--------|
| Request mutation | `POST /Shifts/Request/{slotId}` | 5 | 1 minute |
| Private-token read | `GET /Requests/Status/{token}` and `GET /Actions/{token}` | 30 | 1 minute |
| Assignment-action mutation | `POST /Actions/{token}` | 10 | 1 minute |

Configure ASP.NET Core's built-in partitioned fixed-window limiter. The partition key combines the tier name with `HttpContext.Connection.RemoteIpAddress`; a missing address uses one shared `unknown` key rather than bypassing the limiter. Each tier has an independent bucket and no queue, so the first excess request is rejected immediately. Local proxy-aware integration coverage uses the same processed remote-IP boundary as the application.

The global partition selector returns a no-limit partition for every method/path not listed above. This keeps coordinator pages, health probes, static content, and the public openings list unaffected. Place `UseRateLimiter` after forwarded-header/routing processing and before authentication-independent antiforgery/page execution, so rejected POSTs cannot mutate workflow state or consume action tokens.

The rejection callback returns status 429, content type `text/plain`, a generic `Too many requests. Try again later.` body, and `Retry-After` based on the configured window. It never invokes a token lookup and never varies by route-identifier validity. Logs may include the policy tier and address but never route tokens, form values, volunteer contact information, or token-validity results.

The limiter is intentionally process-local. A distributed or multi-region limiter is outside this issue.

---

## Local Coordinator Authorization Regression

Use the existing local/development authentication path rather than live OIDC. Configure two distinct normalized identities in the local coordinator allowlist and exercise each identity independently against coordinator behavior. Assert that both allowlisted identities can access coordinator pages and that a third identity not on the allowlist remains unauthorized. Do not add a production identity-provider group, live handoff, or startup requirement for two identities.

---

## Security Considerations

- Rate limiting supplements, rather than replaces, antiforgery, token hashing, expiry, single use, authorization, and domain invariants.
- Valid and invalid token paths receive identical throttling responses without database access after a partition is exhausted.
- A missing client address is limited rather than treated as unlimited.
- Local test and smoke output must not contain connection strings, volunteer contacts, raw status tokens, raw action tokens, or authentication secrets.
- No public deployment, Railway resource, production database, backup, or live OIDC account is accessed by local verification.

---

## Performance Considerations

Fixed-window state is in process memory and bounded by active client/tier partitions. Idle partitions expire with their windows. Rejected traffic does not perform page-model or PostgreSQL work. Readiness performs one bounded PostgreSQL check during probes, and migration work runs once outside the serving process.

---

## Steering Alignment

- **Product**: preserves coordinator-owned authoritative workflow state and low-friction accountless volunteer actions; rejected anonymous traffic cannot mutate workflow state.
- **Technology**: retains the .NET 10 modular monolith, PostgreSQL-only persistence behavior, explicit EF Core migrations, and local verification without introducing a deployment provider change.
- **Structure**: keeps health, authentication wiring, rate limiting, and composition in Web; keeps process persistence in Infrastructure; keeps behavior coverage in the existing integration-test project; adds no alternate project or spec root.

---

## Testing Strategy

| Layer | Type | Coverage |
|-------|------|----------|
| Web integration | Local host plus isolated PostgreSQL | Readiness failure, liveness, per-tier exhaustion, independent client/tier buckets, generic 429 response, `Retry-After`, no workflow/token mutation, unaffected health and coordinator routes, and two allowlisted identities. |
| Process integration | Published local Web executable plus isolated PostgreSQL | `--migrate-only` applies pending migrations and exits without listening; migration failure returns non-zero; ordinary startup does not migrate. |
| Compose smoke | Local Docker Compose | PostgreSQL becomes healthy before the one-shot migration, migration completes before Web, and a migration failure prevents Web startup. |
| Verification | Repository-local commands and scenario | Focused unit/integration checks, Compose configuration/smoke, and local endpoint exercise cover every acceptance criterion. No Railway or production check is part of this issue. |

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #14 | 2026-09-03 | Initial production-readiness design |
| #14 | 2026-09-20 | Design narrowed to local runtime safety and anonymous abuse controls |
