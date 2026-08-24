# Contributing

## Project Context

Volunteer Coordinator is a .NET 10 modular monolith for authoritative volunteer shift coverage. Razor Pages provides accountless volunteer journeys and allowlisted coordinator workflows over PostgreSQL. Product work must preserve coordinator-owned schedule state, low-friction volunteer actions, explicit coverage status, and notification-independent state transitions.

## Lifecycle Commands

| Need | Command |
|------|---------|
| Draft a scoped issue | `/sdlc-draft-issue [need]` |
| Write the issue specification | `/sdlc-write-spec #N` |
| Onboard a project | `/sdlc-onboard-project` |
| Reconcile an existing project | `/sdlc-upgrade-project` |
| Execute approved issue specifications | `/sdlc-execute [#N …]` |
| Inspect lifecycle status | `/sdlc-status` |

## Issue and Spec Workflow

Start from a clear GitHub issue containing a user story or bug context, Given/When/Then acceptance criteria, functional requirements, explicit scope and out-of-scope notes, priority, and relevant background.

Feature and bug implementation flows through one issue-owned spec at `specs/{N}-{slug}/`. The directory contains:

- `requirements.md`
- `design.md`
- `tasks.md`
- `feature.gherkin`

Every artifact declares the same owning issue with singular `**Issue**: #N`. Legacy `feature-*` and `bug-*` spec layouts are inputs to `/sdlc-upgrade-project` only. Do not create a spec without a real issue number.

Interactive work normally starts with `/sdlc-draft-issue [need]`, then `/sdlc-write-spec #N`. After the spec is approved, `/sdlc-execute [#N …]` drives implementation, verification, review, exact-head merge, and issue closure. Pull-request creation is intermediate, not completed delivery.

## Steering Expectations

Before drafting an issue, writing a spec, or implementing work, consult:

- `steering/product.md` for users, workflows, invariants, and success outcomes.
- `steering/tech.md` for the .NET 10 stack, modular boundaries, PostgreSQL persistence, security, verification gates, and versioning.
- `steering/structure.md` for the reserved project layout, dependency direction, ownership, naming, and tests.

Explain how the proposed change aligns with all three. Existing code, when application work begins, and reconciled specs are contribution context; neither overrides current steering or an approved active spec.

## Implementation and Verification

Stay within the approved spec. Avoid unrelated refactors, preserve project-owned files, and update every affected caller or contract in a clean cutover. Domain rules belong in Domain, use cases and ports in Application, adapters and EF Core in Infrastructure, and Razor Pages, authentication wiring, validation, presentation, and composition in Web.

Verification evidence must name the command or contract and the observed outcome. Apply the gates in `steering/tech.md` when the solution exists. Summarize relevant tests and failures honestly, and link a non-empty `verification-report.md` when one is produced.

### Evidence Consistency

The managed contribution gate evaluates a connected evidence graph rather than keywords:

- **Issue/spec identity:** an ordinary feature or bug PR uses a current reference such as `Closes #143`; each selected spec directory uses singular `**Issue**: #143`. Numbers only in quoted examples, hidden comments, historical sections, or unrelated specs do not correlate.
- **Exact path evidence:** a task or verification entry can name `scripts/check-gate.mjs` exactly.
- **Directory-prefix evidence:** a task can scope work to `scripts/__tests__/`; `check-gate.mjs` alone is insufficient because another directory may contain that basename.
- **Path-specific behavior evidence:** use `Behavior for scripts/check-gate.mjs: rejects mismatched issue/spec sets` when behavior is the useful trace.
- **Command and outcome:** record both sides, for example, `` `node scripts/check-gate.mjs` — passed (12 cases) ``. Generic statements such as “tests run” or “verification complete” are not specific evidence.
- **Other accepted evidence:** record `AC9: passed`, provide a non-empty `verification-report.md`, or pair a changed path with a `passed`, `failed`, `verified`, or `covered` result.

## Validated Reduced-Evidence Contracts

These modes reduce only named checks after their complete predicate is validated. They are not bypasses.

| Mode | Declaration and validation | Reduced checks | Still required | Invalidating conditions |
|------|----------------------------|----------------|----------------|-------------------------|
| Documentation-only | `SDLC-Exception: docs-only — <non-empty reason>` and every changed path is project documentation | Spec correlation, relevant-path mapping, and specific verification | Current issue linkage, steering artifacts and alignment, guide discoverability, and all other checks | Any source, workflow, script, skill, template, shared reference, spec, ADR, or other non-documentation path |
| Repository rewrite | `SDLC-Exception: repository-rewrite — <non-empty reason>`; title starts `feat!:`; all package/version, public-guide, steering, managed-gate, rewrite-contract, and rewrite-verification paths required by the gate change | Current PR issue/spec identity only | Owned current specs, explicit rewrite contract, durable verification, steering alignment, exact changed-path mapping, specific verification, and guide discoverability | Missing contract path, non-breaking title, unmatched relevant path, missing steering, or missing verification |
| Spec-only write-spec | Title is `docs: approve spec for #N`; that number appears in current PR text; every change is under exactly one matching `specs/{N}-{slug}/` | Steering-alignment text and specific verification | Current issue linkage, spec correlation, steering artifacts, guide discoverability, and all other checks | Any non-spec path, title mismatch, multiple spec directories, or issue mismatch |

Remove an invalid exception or split invalidating implementation changes into a normally evidenced PR. A marker, label, or rationale never overrides incompatible changed paths.

## Pull Request Readiness

Before requesting review, confirm:

- [ ] The executable issue is linked with current issue-closing text.
- [ ] The matching `specs/{N}-{slug}/` and all four artifacts are linked.
- [ ] Product, technical, and structure steering alignment is stated.
- [ ] Changed paths map to task or verification evidence.
- [ ] Implementation remains within approved scope; known gaps are explicit.
- [ ] Verification lists specific commands or acceptance criteria with observed outcomes.
- [ ] Reviewer context explains consequential decisions and remaining risk.
- [ ] The branch is ready for exact-head merge and subsequent issue closure.

## Contribution-Gate Remediation

The managed GitHub Actions contribution gate checks issue, spec, changed-path, steering, verification, and guide evidence. Repair the broken edge named by the failure:

- Add or correct the current issue reference and matching singular spec issue.
- Add the missing canonical spec artifact or correct its `specs/{N}-{slug}/` location.
- Explain alignment with all steering files or restore a missing steering artifact.
- Map every relevant changed path by exact path, containing-directory prefix, or structured behavior evidence.
- Add a specific command-and-outcome, acceptance result, or non-empty verification report.
- Restore this guide if it is missing.
- Remove an invalid reduced-evidence declaration instead of weakening the workflow.

Do not bypass or fork the gate to hide missing evidence. Delivery succeeds only when the reviewed exact head is merged and the owning issue is closed.

## Local Run and Configuration

The application requires .NET 10 and PostgreSQL; no SQLite or in-memory persistence mode exists. On this workstation, the user-local SDK can be selected in PowerShell before running `dotnet`:

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
```

### Docker Compose

Set local-only values in the shell, then start PostgreSQL and the Web process:

```powershell
$env:POSTGRES_PASSWORD = "choose-a-local-password"
$env:COORDINATOR_EMAIL = "coordinator@example.org"
$env:POSTGRES_PORT = "54329"
docker compose up --build
```

Open `http://localhost:8080/development/login`, sign in with the exact allowlisted email, and continue to schedule setup. Compose explicitly enables the Development-only login and opt-in startup migration. It creates no shifts or volunteer data.

### Native Web Process

With the Compose variables set, start only PostgreSQL:

```powershell
docker compose up -d postgres
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__Postgres = "Host=localhost;Port=$env:POSTGRES_PORT;Database=volunteer_coordinator;Username=postgres;Password=$env:POSTGRES_PASSWORD"
$env:Database__MigrateOnStartup = "true"
$env:DevelopmentAuth__Enabled = "true"
$env:Coordinator__AllowedEmails__0 = $env:COORDINATOR_EMAIL
dotnet run --project src/VolunteerCoordinator.Web/VolunteerCoordinator.Web.csproj
```

The opt-in migration setting applies the committed PostgreSQL migration during process startup. Leave it `false` after an environment has a separate migration step. Integration tests start an isolated PostgreSQL container and therefore require a Docker-compatible engine.

### Runtime Variables

| Variable | Required behavior |
|----------|-------------------|
| `ConnectionStrings__Postgres` | Required PostgreSQL connection string in every environment. Supply it as a secret variable. |
| `POSTGRES_PORT` | Optional Docker Compose host port for PostgreSQL; defaults to `54329`. |
| `Coordinator__AllowedEmails__0`, `__1`, ... | Normalized application allowlist layered on authenticated OIDC email claims. At least one coordinator is needed to use coordinator pages. |
| `Oidc__Authority` | Production OIDC issuer/authority. |
| `Oidc__ClientId` | Production OIDC client identifier. |
| `Oidc__ClientSecret` | Production OIDC secret; never commit it. |
| `Database__MigrateOnStartup` | Safe default is `false`; set `true` only for an explicit single-instance migration/startup step. |
| `DevelopmentAuth__Enabled` | Safe default is `false`; honored only when `ASPNETCORE_ENVIRONMENT=Development`. |

Railway builds the root `Dockerfile` and checks `/health`; `/health/ready` additionally verifies PostgreSQL. Configure the PostgreSQL connection, OIDC values, and allowlist as Railway variables. Never enable Development authentication in Railway or commit `.env` files, passwords, client secrets, raw volunteer action tokens, or production schedule data.
