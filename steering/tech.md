# Volunteer Coordinator Technical Steering

This document defines the rebuild technology, architecture, security, persistence, verification, and versioning constraints.

## Architecture

Volunteer Coordinator is a .NET modular monolith: one deployable ASP.NET Core web process with explicit Domain, Application, Infrastructure, and Web boundaries. Razor Pages delivers the user interface. Background work is hosted in-process until measured operational needs justify a separate worker. The first implementation issue owns creation of the solution and projects; onboarding does not scaffold them.

## Technology Stack

| Concern | Technology | Constraint |
|---------|------------|------------|
| Runtime | .NET 10 LTS | Target `net10.0`; supported through November 14, 2028. |
| Web | ASP.NET Core Razor Pages | One deployable web process. |
| Persistence | Entity Framework Core | PostgreSQL provider only for runtime persistence behavior. |
| Database | PostgreSQL | Sole database in every environment that exercises persistence. |
| Testing | xUnit | Unit and PostgreSQL-backed integration tests. |
| Packaging | Docker | Reproducible application image. |
| Hosting | Railway | Configuration and secrets supplied by Railway variables. |

## Layer and Process Constraints

- Domain contains business rules and state transitions and has no infrastructure dependency.
- Application contains use cases and ports and depends only on Domain.
- Infrastructure implements persistence and external adapters against Application and Domain contracts.
- Web owns Razor Pages, authentication wiring, validation, presentation, and composition.
- One web process is the composition root and initial deployment unit.
- In-process hosted services may perform background work. A separate worker requires measured operational evidence and an approved issue and spec.

## Identity and Security

- Coordinators authenticate through OpenID Connect (OIDC) and must also appear on an application allowlist.
- Coordinator operations require authorization and produce an audit record.
- Volunteers do not require persistent accounts for the first releasable workflow. A request records contact details.
- Confirm, decline, and cancel operations use cryptographically random action tokens that are hashed at rest, expiring, and single-use.
- Secrets come only from environment variables or Railway variables. Committed settings must never contain secrets.
- User input is validated at the Web boundary; authorization and domain invariants are also enforced at the use-case or domain boundary.

## Persistence and Time

- PostgreSQL is the sole runtime database in all environments that exercise persistence behavior.
- Integration tests use isolated PostgreSQL containers. Do not use the EF Core in-memory provider or SQLite as a persistence substitute.
- Database state is authoritative for schedules, publication, requests, assignments, confirmations, and cancellations.
- Store all instants as UTC. Convert to the applicable display time zone only at presentation boundaries.
- State transitions and their audit records commit independently of notification delivery. A notification failure must not roll back or misreport a successful transition.

## External Services

Transactional email is an Application port with an Infrastructure adapter. No email provider is selected during onboarding. Provider selection, delivery policy, and operational configuration require an approved issue and spec.

OIDC authority, client configuration, coordinator allowlist, PostgreSQL connection information, and email credentials are runtime configuration supplied through environment or Railway variables.

## Verification Standards

- Unit tests cover domain rules and application behavior without framework plumbing.
- Integration tests exercise persistence behavior against an isolated PostgreSQL instance.
- Tests are deterministic, isolated, and assert observable behavior.
- Gherkin files under `specs/{N}-{slug}/feature.gherkin` are acceptance contracts until a BDD runner is deliberately introduced by an approved issue.

## Verification Gates

Run these gates from the repository root after the future solution exists, in order:

| Gate | Command | Success |
|------|---------|---------|
| Formatting | `dotnet format --verify-no-changes VolunteerCoordinator.sln` | Exit code 0 |
| Release build | `dotnet build VolunteerCoordinator.sln -c Release --no-restore` | Exit code 0 |
| Tests | `dotnet test VolunteerCoordinator.sln -c Release --no-build` | Exit code 0 |

These commands are the hard verification gates for implementation changes. The build gate assumes dependencies were restored by the implementation workflow before verification.

## Versioning

Root `VERSION` is the current version source of truth.

| Issue label or decision | Bump | Rule |
|-------------------------|------|------|
| `bug` | patch | Backwards-compatible defect correction. |
| `enhancement` | minor | Backwards-compatible capability. |
| Approved explicit major decision | major | Allowed only by the exact line below in approved requirements or design. |

The required major-bump line is:

`**Version bump**: major`

No other wording authorizes a major bump.

## Lifecycle and Spec Authority

Executable work starts from one GitHub issue and uses the canonical directory `specs/{N}-{slug}/`. Every artifact in that directory declares one matching issue with singular `**Issue**: #N`.

| Need | Command |
|------|---------|
| Draft a scoped issue | `/sdlc-draft-issue [need]` |
| Write the issue specification | `/sdlc-write-spec #N` |
| Onboard a project | `/sdlc-onboard-project` |
| Reconcile an existing project | `/sdlc-upgrade-project` |
| Execute approved issue specifications | `/sdlc-execute [#N …]` |
| Inspect lifecycle status | `/sdlc-status` |

## References

- Product direction: `steering/product.md`
- Code organization: `steering/structure.md`
- General project context: `AGENTS.md`
