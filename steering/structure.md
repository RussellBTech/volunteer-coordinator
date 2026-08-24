# Volunteer Coordinator Code Structure Steering

This document defines the implemented modular-monolith layout, dependency direction, ownership, and naming conventions. The initial application source was created by an approved implementation issue; subsequent changes require their own approved issue and spec.

## Reserved Project Layout

```text
VolunteerCoordinator.sln
src/
├── VolunteerCoordinator.Domain/
├── VolunteerCoordinator.Application/
├── VolunteerCoordinator.Infrastructure/
└── VolunteerCoordinator.Web/
tests/
├── VolunteerCoordinator.UnitTests/
└── VolunteerCoordinator.IntegrationTests/
specs/
└── {N}-{slug}/
```

Reserved source and test project paths:

- `src/VolunteerCoordinator.Domain/`
- `src/VolunteerCoordinator.Application/`
- `src/VolunteerCoordinator.Infrastructure/`
- `src/VolunteerCoordinator.Web/`
- `tests/VolunteerCoordinator.UnitTests/`
- `tests/VolunteerCoordinator.IntegrationTests/`

The first approved implementation spec may create only the projects it needs from this reserved layout. Do not introduce alternate roots, a second architecture, or placeholder projects.

## Dependency Direction

```text
VolunteerCoordinator.Web ───────────────→ VolunteerCoordinator.Application ─→ VolunteerCoordinator.Domain
          │                                              ↑
          └─ composition root                            │
VolunteerCoordinator.Infrastructure ─────────────────────┘
          └──────────────────────────────────────────────→ VolunteerCoordinator.Domain

VolunteerCoordinator.Domain → no project
```

- Web may reference Application and wires Infrastructure at the composition root.
- Infrastructure may reference Application and Domain to implement ports and persistence.
- Application may reference Domain.
- Domain references no project.
- Composition occurs only in Web.
- Project references must not reverse these arrows or bypass Application use cases from Web.

## Layer Ownership

| Layer | Owns | Does not own |
|-------|------|--------------|
| Domain | Rules, invariants, state transitions, domain types | EF Core, Razor Pages, authentication wiring, external service SDKs |
| Application | Use cases, commands and queries, ports, orchestration, authorization requirements independent of a provider | Razor presentation, EF mappings, vendor clients |
| Infrastructure | EF Core, PostgreSQL mappings and migrations, OIDC-supporting persistence, email and other external adapters | Product rules, page behavior, composition |
| Web | Razor Pages, request validation, authentication and authorization wiring, presentation, dependency composition | Database rules, provider logic embedded in pages |

Feature-oriented folders group related behavior inside each layer. For example, shift setup use cases belong together under an owning feature folder in Application, with their domain rules under the corresponding Domain feature. Layer boundaries remain explicit.

## Test Ownership

- `tests/VolunteerCoordinator.UnitTests/` mirrors Domain and Application ownership and asserts rules and use-case behavior.
- `tests/VolunteerCoordinator.IntegrationTests/` mirrors Infrastructure and Web integration ownership and uses isolated PostgreSQL for persistence behavior.
- Tests assert observable behavior, boundaries, invariants, transitions, precedence, and real errors rather than framework plumbing.
- Test helpers stay inside the narrowest owning test project; production projects do not reference test code.

## C# and .NET Naming

| Element | Convention | Example |
|---------|------------|---------|
| Namespaces, types, public members | PascalCase | `PublishSchedule` |
| Files | Match the single public type in PascalCase | `PublishSchedule.cs` |
| Interfaces | PascalCase prefixed with `I` | `IShiftRepository` |
| Async methods | PascalCase suffixed with `Async` | `PublishAsync` |
| Local variables and parameters | camelCase | `scheduleId` |
| Private fields | `_` plus camelCase | `_shiftRepository` |

Additional constraints:

- One public type per file.
- Nullable reference types are enabled in every project.
- Use async APIs for I/O and propagate `CancellationToken` through use cases and adapters.
- Prefer explicit domain types and transitions over page-local status mutation.
- Keep provider and EF Core details in Infrastructure.
- Keep validation messages and presentation models in Web; keep enforceable business invariants in Domain.

## File and Folder Rules

- Razor Page models and views remain paired under feature-oriented folders in `VolunteerCoordinator.Web`.
- EF Core configurations and migrations live under `VolunteerCoordinator.Infrastructure`.
- Application ports live with the feature that consumes them unless genuinely shared across features.
- Shared folders require demonstrated cross-feature ownership; do not create generic Helpers, Common, or Utilities dumping grounds.
- Generated build output and local runtime state remain untracked.

## Lifecycle and Spec Authority

Project-root `specs/` is the canonical archive. Executable specs use `specs/{N}-{slug}/`, where `N` is the owning GitHub issue number. Every spec artifact uses singular `**Issue**: #N` matching the directory. Do not create issue-less, cumulative, or aggregate spec directories.

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
- Technical constraints: `steering/tech.md`
- General project context: `AGENTS.md`
