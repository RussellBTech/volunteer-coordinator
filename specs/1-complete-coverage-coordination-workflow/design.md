# Design: Complete Coverage Coordination Workflow

**Issue**: #1
**Date**: 2026-08-23
**Status**: Approved
**Author**: RussellBTech

## Architecture

Create the reserved .NET 10 modular monolith:

- `VolunteerCoordinator.Domain`: aggregates, value rules, enums, transition methods, domain errors; no project references.
- `VolunteerCoordinator.Application`: use-case services, DTOs, ports for persistence, clock, token hashing/generation, identity, notifications, and transactions; references Domain.
- `VolunteerCoordinator.Infrastructure`: EF Core PostgreSQL, migrations, repository/unit-of-work implementations, cryptographic token services, notification attempt adapter; references Application and Domain.
- `VolunteerCoordinator.Web`: Razor Pages, input validation, authorization wiring, OIDC/cookie configuration, Development-only login, presentation, and composition; references Application and Infrastructure.
- Unit and Integration test projects mirror ownership. Integration tests use a real isolated PostgreSQL database.

Web must not query `DbContext` directly. Page models call Application use cases. Infrastructure implements Application ports. Domain methods own state transitions.

## Clean Cutover

Delete `VSMS.sln`, `src/VSMS.*`, `tests/VSMS.*`, `seed-test-data.sql`, and `docs/ux-audit-decisions.md`. Install `.github/`, `AGENTS.md`, `CONTRIBUTING.md`, `VERSION`, and `steering/`. Replace root deployment artifacts with the new application versions. Introduce no compatibility shim or legacy-data importer.

## Domain Model

### Shift

- `Guid Id`
- `string Title` (1–120)
- `string? Location` (max 200)
- `string? Notes` (max 1000; coordinator-only)
- `DateTimeOffset StartsAtUtc`
- `DateTimeOffset EndsAtUtc`
- `bool IsActive`
- `DateTimeOffset? PublishedAtUtc`
- `uint Version` mapped to PostgreSQL `xmin` for optimistic concurrency
- collection of `ShiftSlot`

Creation requires `EndsAtUtc > StartsAtUtc` and a backup-slot count from 0 through 2. Create exactly one primary slot at position 1 and the requested numbered backup slots. Editing may alter descriptive fields and future UTC interval. Deactivation hides all slots from public discovery without deleting history. Publication requires active state and a future end time.

### ShiftSlot

- `Guid Id`, `Guid ShiftId`
- `SlotKind Kind`: `Primary` or `Backup`
- `int Position`: primary is 1; backup is 1 or 2
- assignment history collection

A database unique constraint covers `(ShiftId, Kind, Position)`.

### Volunteer

- `Guid Id`
- `string Name`
- `string Email`, `string NormalizedEmail`
- `string? Phone`
- created/updated UTC instants

Normalized email is trimmed and upper-invariant. A unique constraint prevents duplicate volunteer records.

### ShiftRequest

- `Guid Id`, `Guid ShiftSlotId`, `Guid VolunteerId`
- `RequestStatus`: `Pending`, `Approved`, `Rejected`, `Superseded`
- requested/resolved UTC instants and optional resolving coordinator email
- private status-token hash and expiry

A partial unique PostgreSQL index permits only one pending request per `(ShiftSlotId, VolunteerId)`. Status tokens are opaque, stored only as SHA-256 hashes, expire after 30 days, and may be reused only for read-only status lookup.

### Assignment

- `Guid Id`, `Guid ShiftSlotId`, `Guid VolunteerId`, optional source request
- `AssignmentStatus`: `Assigned`, `Confirmed`, `Declined`, `Cancelled`, `Reassigned`
- assigned/confirmed/ended UTC instants and coordinator email

Exactly one active assignment (`Assigned` or `Confirmed`) may exist per slot. A volunteer may hold at most one active slot on the same shift. Approving or directly assigning supersedes the previous active assignment within one transaction and records audit entries. Decline/cancel ends the assignment and reopens the slot.

### ActionToken

- `Guid Id`, `Guid AssignmentId`
- `VolunteerAction`: `Confirm`, `Decline`, `Cancel`
- `byte[] TokenHash`
- created/expiry/used UTC instants

Generate 32 random bytes and encode Base64URL. Store only SHA-256. Compare hashes in constant time. Tokens expire after seven days by default, are single-use, and are bound to the active assignment. Regeneration invalidates unused tokens of the same action.

### AuditEntry and NotificationAttempt

Audit entries capture UTC timestamp, coordinator identity or volunteer token actor, action, entity kind/id, and structured detail. `NotificationAttempt` records transition-linked notification kind, destination, state (`Pending`, `Succeeded`, `Failed`), timestamps, and a safe error summary. It never stores raw action tokens. The initial adapter records pending/unavailable state; coordinator-only link generation delivers raw links once outside notification persistence.

## Application Use Cases

- Schedule: list, create, edit with expected version, deactivate, publish.
- Public openings: query future active published slots without active assignment.
- Requests: submit and return one-time status URL; query status by hashed opaque token.
- Coordinator requests: list, approve, reject.
- Assignments: direct assign, reassign, list eligible volunteers, generate fresh one-time action links.
- Volunteer actions: inspect token without consuming; confirm POST; decline POST; cancel POST.
- Coverage: query future published slots classified as `Uncovered`, `Unconfirmed`, or `Confirmed`, ordered by start time and severity.

Every command uses a unit-of-work transaction around domain state and audit persistence. Notification dispatch occurs after commit and records its own result; its failure is returned as a warning, never as command failure.

## Web Surface

### Public

- `GET /` redirects to `/Shifts`.
- `GET /Shifts` lists published openings and textual slot/status badges.
- `GET|POST /Shifts/Request/{slotId}` submits contact details and shows the private status link once.
- `GET /Requests/Status/{token}` shows request and resulting assignment status.
- `GET|POST /Actions/{token}` shows action consequences and consumes a valid token only on POST.
- `GET /health` uses ASP.NET Core health checks and verifies process health; database readiness is separately exposed as `/health/ready`.

### Coordinator

All `/Coordinator` pages require `CoordinatorOnly` policy.

- `/Coordinator/Schedule`: zero state, list, create, edit, deactivate, publish.
- `/Coordinator/Requests`: pending/history, approve, reject.
- `/Coordinator/Coverage`: uncovered/unconfirmed/confirmed view with assign/reassign actions.
- `/Coordinator/Assignments/Links/{assignmentId}`: generate and display fresh raw confirm, decline, and cancel URLs once.
- `/Coordinator/Audit`: recent auditable actions.

Use antiforgery protection on mutations, POST/Redirect/GET, server-side validation, accessible validation summaries, status text plus icons/colors, and responsive Bootstrap-compatible styling without JavaScript-dependent correctness.

## Authentication

Configure cookie authentication with OIDC challenge. Required runtime settings are OIDC authority/client ID/client secret and `Coordinator__AllowedEmails__N`. The allowlist policy normalizes the authenticated email and rejects absent/non-allowlisted identities.

A Development-only authentication endpoint may be enabled only when both `IHostEnvironment.IsDevelopment()` and `DevelopmentAuth__Enabled=true`. It signs in only an email already in the same allowlist. No development endpoint is mapped otherwise.

## Persistence

Use Npgsql EF Core 10. Migrations live in Infrastructure and are applied explicitly by deployment/startup tooling, not silently by public requests. Use `timestamptz` and `DateTimeOffset` for instants. Add indexes for opening queries, coverage urgency, token hashes, normalized email, pending requests, and active assignments. Enforce the two partial uniqueness rules in PostgreSQL in addition to domain checks.

Integration tests create a unique PostgreSQL database per test collection or use a container fixture, apply migrations, and drop it after completion. Tests must not reference SQLite or `Microsoft.EntityFrameworkCore.InMemory`.

## Deployment and Configuration

- Multi-stage `Dockerfile` builds/publishes with `mcr.microsoft.com/dotnet/sdk:10.0` and runs on `aspnet:10.0` as a non-root user.
- `compose.yaml` starts PostgreSQL and the Web app for local execution.
- `railway.toml` points to the Dockerfile and `/health`.
- `appsettings.json` contains safe defaults only; secrets and connection strings come from environment/Railway variables.
- Add a concise local run section to `CONTRIBUTING.md`; do not create `README.md` because steering records it as absent.

## Verification

Use the exact steering gates plus Docker and behavioral smoke verification. The Gherkin file remains a human acceptance contract; xUnit tests cover domain and persistence behavior directly.
