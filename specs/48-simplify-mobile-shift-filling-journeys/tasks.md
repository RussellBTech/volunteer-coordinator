# Tasks: Simplify Mobile Shift-Filling Journeys

**Issue**: #48
**Date**: 2026-09-23
**Status**: Approved
**Author**: Volunteer Coordinator product team

## Approved implementation sequence

- [ ] T1 — Update `src/VolunteerCoordinator.Web/Pages/Shared/_Layout.cshtml` and `src/VolunteerCoordinator.Web/wwwroot/css/site.css`: show Home/Work/Schedule or Open shifts immediately; move every other existing destination/sign-out into one native disclosure; preserve all routes, targets, keyboard focus, and POST antiforgery.
- [ ] T2 — Update `src/VolunteerCoordinator.Web/Pages/Shifts/Index.cshtml`, `src/VolunteerCoordinator.Web/Pages/Shifts/Request.cshtml`, `src/VolunteerCoordinator.Web/Pages/Requests/Status.cshtml`, `src/VolunteerCoordinator.Web/Pages/Recurring/Index.cshtml`, `src/VolunteerCoordinator.Web/Pages/Recurring/Hub.cshtml` and CSS: compact local-time card/request context; link existing non-enumerating `/Commitments/Recover` from browse; expose one policy-correct primary CTA and valid hub action; keep recurring exact-date review as the only pre-preview action and submit as the post-preview primary; retain privacy, status, validation and full optional details.
- [ ] T3 — Update `src/VolunteerCoordinator.Web/Pages/Coordinator/`, `src/VolunteerCoordinator.Web/wwwroot/css/site.css`, and `src/VolunteerCoordinator.Application/VolunteerCoordinatorService.cs`: prioritize Home/Work/Coverage/Requests interventions, make protected Work/Go links reach the selected slot or request, compact commitment context, connect existing multi-action forms to server handlers with preserved reviewed state, and use the same policy-consequence text for preview and save. No workflow command or query change.
- [ ] T4 — Browser-smoke actual volunteer and coordinator UI at 320px and desktop: no overflow, native keyboard/no-script navigation, browse→request/claim→private hub, recurring preview→publish→signup→confirm, Home→Work→selected intervention, policy previews, local time, and authorization. `tests/VolunteerCoordinator.IntegrationTests/Issue17CoordinatorWebIntegrationTests.cs` guards rendered navigation and edit prefill; `tests/VolunteerCoordinator.IntegrationTests/Issue20RecurringWebIntegrationTests.cs` guards the rendered recurring preview/create/publish buttons and reviewed version. Main owns run-once format/build/test gates and records observed outcomes.
- [ ] T5 — Integration owner Main applies the one enhancement minor increment to root `VERSION` and records `CHANGELOG.md` entry exactly once, maps issue/spec paths to delivery evidence, reviews exact head, and closes #48 only after approved lifecycle delivery. This presentation agent must not mutate those two files.

## Scope and dependencies

#17 Home/previews, #18 capability hub, #21 policy/recurring, and #22 Work projection are existing contracts. Tasks modify presentation only; Domain, Application, Infrastructure, migrations, routes, access policy, and capability state are unchanged. Retain every existing path and use approved `requirements.md`/`design.md`/`feature.gherkin` as acceptance authority.
