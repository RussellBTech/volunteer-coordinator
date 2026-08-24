# AGENTS.md

Volunteer Coordinator is a .NET 10 modular monolith with coordinator-owned schedules, PostgreSQL-authoritative workflow state, accountless volunteer actions through expiring single-use links, and authenticated auditable coordinator actions. The implemented solution follows the approved `src/` and `tests/` layer layout; subsequent behavior and layout changes require a real issue and approved spec.

Consult `steering/product.md`, `steering/tech.md`, and `steering/structure.md` before changing product behavior, technology, or repository layout.

## Lifecycle Commands

| Need | Command |
|------|---------|
| Draft a scoped issue | `/sdlc-draft-issue [need]` |
| Write the issue specification | `/sdlc-write-spec #N` |
| Onboard a project | `/sdlc-onboard-project` |
| Reconcile an existing project | `/sdlc-upgrade-project` |
| Execute approved issue specifications | `/sdlc-execute [#N …]` |
| Inspect lifecycle status | `/sdlc-status` |

<!-- nmg-sdlc-managed: spec-context -->
## nmg-sdlc Spec Context

For SDLC work, project-root `specs/` is the canonical BDD archive. Specs use directories of the form `specs/{N}-{slug}/` where `N` is the GitHub issue number. Always identify the active spec first (leading directory number must match the issue and every file must declare singular `**Issue**: #N`), then use bounded relevant-spec discovery to load only the neighboring specs that can affect the change. Do not load the full archive by default. Legacy `.codex/specs/` directories are inputs to `/sdlc-upgrade-project` only.
<!-- /nmg-sdlc-managed -->
