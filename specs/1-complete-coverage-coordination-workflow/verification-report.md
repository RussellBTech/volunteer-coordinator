# Verification Report: Complete Coverage Coordination Workflow

**Issue**: #1
**Date**: 2026-08-23
**Status**: Passed

## Automated Gates

- `dotnet format --verify-no-changes VolunteerCoordinator.sln` — passed with exit code 0.
- `dotnet build VolunteerCoordinator.sln -c Release --no-restore` — passed with 0 warnings and 0 errors.
- `dotnet test VolunteerCoordinator.sln -c Release --no-build` — passed: 14 unit tests and 6 PostgreSQL integration tests; 20 total, 0 failed, 0 skipped.
- `dotnet list VolunteerCoordinator.sln package --vulnerable --include-transitive --no-restore` — passed: no vulnerable packages reported for any project.
- `docker build -t volunteer-coordinator:verification .` — passed.
- `docker compose config --quiet` with local-only required variables — passed.
- Container `/health` — returned `Healthy`.
- Container `/health/ready` against PostgreSQL — returned `Healthy`.
- `git diff --check` — passed with exit code 0.

## Behavioral Scenario

The actual Razor Pages surface was browser-driven against the Release build and PostgreSQL:

1. Public `/Shifts` displayed the accountless no-openings state.
2. The explicitly enabled Development-only login accepted the allowlisted coordinator.
3. Schedule setup displayed the coordinator zero state.
4. The coordinator created a future shift with one primary and one backup slot, corrected its location, and published it.
5. After coordinator sign-out, public discovery displayed both published openings with textual `Open`, `Primary`, and `Backup 1` states.
6. A volunteer submitted contact details for the primary slot and received a once-displayed private request-status URL plus a separate notification-unavailable warning.
7. The status URL displayed `Pending` without coordinator authentication.
8. The coordinator approved the request. Coverage monitoring displayed the primary slot as `Unconfirmed` and the backup slot as `Uncovered` with direct intervention links.
9. The coordinator generated fresh confirm, decline, and cancel links; raw links were displayed only on that response.
10. Signed out, the volunteer confirmed through the anonymous action page. Reusing the confirm link displayed the invalid/expired/already-used state.
11. The private status URL then displayed request `Approved` and assignment `Confirmed`.
12. The volunteer cancelled through the anonymous cancel link; the primary slot returned to public discovery.
13. The coordinator audit page showed creation, correction, publication, request, approval, link generation, confirmation, and cancellation entries with UTC timestamps and actors.
14. The coordinator deactivated the shift; schedule setup displayed `Inactive`, and the public-discovery removal message was shown.

Decline, reassignment, duplicate-pending rejection, PostgreSQL uniqueness, notification-failure independence, and allowlist denial are additionally covered by the passing unit and PostgreSQL integration suites.

## Fixed During Verification

- Regenerated the initial EF Core migration from the actual model so PostgreSQL migration drift detection passes.
- Made OIDC registration conditional on complete authority/client configuration so Development-only local execution does not initialize an invalid remote scheme.
- Added `.dockerignore` so Windows build artifacts cannot overwrite Linux restore assets in the container build.
- Pinned safe transitive package overrides for `System.Security.Cryptography.Xml` and `SSH.NET`; the final vulnerability audit is clean.

## Changed-Path Evidence

- `src/VolunteerCoordinator.Domain/` — verified domain transition tests.
- `src/VolunteerCoordinator.Application/` — verified complete transactional workflow integration.
- `src/VolunteerCoordinator.Infrastructure/` — verified migrations, PostgreSQL constraints, secure tokens, notification isolation, and readiness.
- `src/VolunteerCoordinator.Web/` — verified public and coordinator journeys in the actual browser surface.
- `tests/VolunteerCoordinator.UnitTests/` — 14 passed.
- `tests/VolunteerCoordinator.IntegrationTests/` — 6 passed against isolated PostgreSQL.
- `Dockerfile` and `.dockerignore` — image built and started.
- `compose.yaml` — configured stack validated.
- `railway.toml` — health target exercised through the built image.
