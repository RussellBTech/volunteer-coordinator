# Tasks: Complete Coverage Coordination Workflow

**Issue**: #1
**Date**: 2026-08-23
**Status**: Approved
**Author**: RussellBTech

## Implementation Tasks

### T001: Replace the legacy repository baseline

**File(s)**: `VSMS.sln`; `src/VSMS.*/`; `tests/VSMS.*/`; `seed-test-data.sql`; `docs/ux-audit-decisions.md`; `.github/`; `AGENTS.md`; `CONTRIBUTING.md`; `VERSION`; `steering/`
**Type**: Delete / Create / Modify
**Depends**: None
**Acceptance**:
- [ ] Legacy solution, projects, jobs, seed script, and obsolete UX artifact are absent
- [ ] current lifecycle and steering contract is present
- [ ] no compatibility shim or importer remains.

### T002: Create the modular solution

**File(s)**: `VolunteerCoordinator.sln`; `src/VolunteerCoordinator.*/`; `tests/VolunteerCoordinator.*/` project files
**Type**: Create
**Depends**: T001
**Acceptance**:
- [ ] All six projects target `net10.0`
- [ ] nullable, implicit usings, and analyzers are enabled
- [ ] references match Web → Application, Infrastructure → Application/Domain, Application → Domain, Domain → none.

### T003: Implement domain workflow rules

**File(s)**: `src/VolunteerCoordinator.Domain/`
**Type**: Create
**Depends**: T002
**Acceptance**:
- [ ] Shift, slot, volunteer, request, assignment, token, audit, and notification models enforce the approved transitions and uniqueness invariants without infrastructure dependencies.

### T004: Implement application use cases and ports

**File(s)**: `src/VolunteerCoordinator.Application/`
**Type**: Create
**Depends**: T003
**Acceptance**:
- [ ] Schedule, request, assignment, action, coverage, audit, and notification use cases expose validation, transaction, clock, token, identity, and persistence ports without Web/EF dependencies.

### T005: Implement PostgreSQL infrastructure

**File(s)**: `src/VolunteerCoordinator.Infrastructure/`
**Type**: Create
**Depends**: T003, T004
**Acceptance**:
- [ ] EF Core/Npgsql mappings and migrations enforce pending-request and active-assignment uniqueness
- [ ] repositories, unit of work, token hashing/generation, notification attempts, and dependency registration satisfy Application ports
- [ ] no SQLite or EF in-memory provider exists.

### T006: Implement public volunteer pages

**File(s)**: `src/VolunteerCoordinator.Web/Pages/` excluding `Coordinator/`
**Type**: Create
**Depends**: T004, T005
**Acceptance**:
- [ ] Published openings, request submission, opaque request status, and confirm/decline/cancel journeys are accountless, validated, and expose clear success/error states.

### T007: Implement coordinator pages

**File(s)**: `src/VolunteerCoordinator.Web/Pages/Coordinator/`
**Type**: Create
**Depends**: T004, T005
**Acceptance**:
- [ ] Zero-state setup, create/edit/deactivate/publish, decisions, assignment/reassignment, one-time link generation, coverage monitoring, and audit history are available to authorized coordinators.

### T008: Wire security and application composition

**File(s)**: `src/VolunteerCoordinator.Web/Program.cs`; authentication/authorization/error/health code
**Type**: Create / Modify
**Depends**: T004, T005, T006, T007
**Acceptance**:
- [ ] Cookie/OIDC plus normalized allowlist protects coordinator paths
- [ ] Development-only login is explicit
- [ ] antiforgery and error handling are active
- [ ] `/health/live` and `/health/ready` expose deployment health.

### T009: Add accessible responsive presentation

**File(s)**: `src/VolunteerCoordinator.Web/Pages/`; `src/VolunteerCoordinator.Web/wwwroot/`
**Type**: Create / Modify
**Depends**: T006, T007, T008
**Acceptance**:
- [ ] Every visible state has text
- [ ] validation summaries and actionable empty states are present
- [ ] core correctness has no JavaScript dependency.

### T010: Add domain and application tests

**File(s)**: `tests/VolunteerCoordinator.UnitTests/`
**Type**: Create
**Depends**: T003, T004
**Acceptance**:
- [ ] Tests cover interval/publication rules, duplicate requests, assignment/reassignment, token expiry/single use, and decline/cancel reopening as observable behavior.

### T011: Add PostgreSQL workflow tests

**File(s)**: `tests/VolunteerCoordinator.IntegrationTests/`
**Type**: Create
**Depends**: T005, T006, T007, T008
**Acceptance**:
- [ ] Isolated PostgreSQL tests cover migrations, published filtering, uniqueness constraints, token hashing/consumption, notification-failure independence, and authorization boundaries.

### T012: Add packaging and runtime configuration

**File(s)**: `Dockerfile`; `.dockerignore`; `compose.yaml`; `railway.toml`; `CONTRIBUTING.md`; `VERSION`
**Type**: Create / Modify
**Depends**: T002, T008
**Acceptance**:
- [ ] Docker builds the .NET 10 Web process as non-root
- [ ] Compose provides local PostgreSQL
- [ ] Railway health/config is valid
- [ ] environment documentation contains no secrets
- [ ] `VERSION` is `0.2.0`.

## Verification

- [ ] Run `dotnet format --verify-no-changes VolunteerCoordinator.sln`.
- [ ] Run `dotnet build VolunteerCoordinator.sln -c Release --no-restore` after restore.
- [ ] Run `dotnet test VolunteerCoordinator.sln -c Release --no-build` against isolated PostgreSQL.
- [ ] Run `docker build -t volunteer-coordinator:verification .`.
- [ ] Start PostgreSQL plus the Web process, use Development-only allowlisted login, and behaviorally verify coordinator zero state → create/edit/publish → volunteer browse/request/status → coordinator approve/assign → generate link → volunteer confirm/decline/cancel → coverage/audit visibility.
- [ ] Verify an unavailable notification adapter does not roll back the exercised workflow transition.
- [ ] Run `git diff --check` and confirm no generated binaries, databases, logs, secrets, or unrelated workflows are present.

## Path Evidence

Behavior for `src/VolunteerCoordinator.Domain/`: owns all workflow invariants and state transitions.

Behavior for `src/VolunteerCoordinator.Application/`: exposes use cases and ports without web or provider dependencies.

Behavior for `src/VolunteerCoordinator.Infrastructure/`: provides PostgreSQL persistence and external adapters.

Behavior for `src/VolunteerCoordinator.Web/`: provides the full public and coordinator Razor Pages journeys and composition root.

Behavior for `tests/VolunteerCoordinator.UnitTests/`: verifies domain and application observable contracts.

Behavior for `tests/VolunteerCoordinator.IntegrationTests/`: verifies real PostgreSQL persistence and workflow boundaries.

Behavior for `Dockerfile`: produces the deployable .NET 10 web image.

Behavior for `compose.yaml`: runs the local Web and PostgreSQL environment.

Behavior for `railway.toml`: defines Railway deployment and health behavior.
