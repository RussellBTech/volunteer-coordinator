# Requirements: Simplify Mobile Shift-Filling Journeys

**Issue**: #48
**Date**: 2026-09-23
**Status**: Approved
**Author**: Volunteer Coordinator product team

## User story

As an occasional mobile volunteer or rotating coordinator, I want the next shift-filling action immediately visible, so I can claim or request an opening and resolve coverage work without navigating a wall of links.

## Background and steering alignment

At 320 CSS pixels the 11 coordinator header links wrap into a long list before any content; volunteer openings and coordinator work expose verbose context before the next step. Existing #17 coordinator guidance, #18 private hub, #21 direct-claim/recurring policy, and #22 prioritized work remain authoritative. Product steering prioritizes low-friction mobile discovery, self-service, and coverage intervention; technical steering places presentation in Web and protects authorization, PostgreSQL state, local time, and token lifecycle; structure steering keeps Razor pages in existing feature folders. This issue changes presentation and unifies one existing Application policy-consequence message; it does not change workflow state or policy eligibility.

## Acceptance criteria

### AC1: Find and act on an opening
Given a volunteer at 320 CSS pixels opens published one-time shifts, when they scan openings, then each card displays title, group-local start and slot, available text status, policy outcome, location when present, and one obvious request-or-claim link before optional full details and instructions. A visible shortcut reaches recurring service even when many one-time occurrences precede it; recurring signup shows an understandable review-before-submit and private hub action flow with plain-language, chronological status. The page has no horizontal scroll or JavaScript-only core action.

### AC2: Understand submission and status
Given a volunteer follows an opening, when they enter contact details, then the selected shift, approval/direct-claim consequence, mandatory privacy notice, and one clearly named submit action remain visible. After submission, the existing private link handoff and the text status/action choices on the hub remain clear; confirm/decline/cancel eligibility and deadlines do not change.

### AC3: Navigate without displacing work
Given a coordinator opens Home, Work, or Schedule at 320 CSS pixels or by keyboard/screen reader, when they view the header, then Home, Work, and Schedule are immediately reachable and all secondary coordinator destinations, public openings, and sign-out remain available behind a native labelled disclosure. The volunteer header similarly exposes public openings directly and sign-in/privacy secondarily. No JavaScript is needed to operate the menu.

### AC4: Fill a shift from prioritized work
Given a coordinator has actionable work, when they open Home then Work, then the highest-priority item is encountered before optional filters and each queue card has one action labelled for its actual intervention. An uncovered item reaches the selected slot's Assign page and a pending request reaches the selected row. On Coverage an uncovered slot emphasizes Assign volunteer; on Requests a pending eligible request emphasizes Approve and assign, while decline/replacement/access/cancellation remain available. Group-local commitment context and textual state remain visible.

### AC5: Preserve safety and routes
Given any user uses existing deep links, previews, and recovery, when the presentation changes, then routes, authorization, antiforgery, audit, privacy disclosure, server-rendered consequence previews, local-time formatting, authoritative state, and capability lifetimes/deadlines are unchanged. Focus order, 44px targets, and text status work at 320 pixels and with keyboard or assistive technology.

## Functional requirements

| ID | Requirement |
|----|-------------|
| FR1 | Replace the long header link wrap with compact primary route links and one native `details/summary` containing every secondary link, including sign-out; never duplicate or remove a destination. |
| FR2 | Place scannable local-time context, policy, availability, and a single primary request/claim action high in each volunteer opening; keep complete context available without JavaScript. |
| FR3 | Clarify the request/claim form and private hub hierarchy without changing posted values, token handling, permission/deadline checks, or privacy text. |
| FR4 | Place authoritative Work results before optional filters; label queue links for the specific intervention and visually distinguish primary from secondary coordinator actions. |
| FR5 | Ensure responsive CSS avoids page-level horizontal overflow at 320 CSS pixels; retain keyboard focus indication, semantic headings/landmarks, and readable text states. |
| FR6 | Keep recurring signup preview and exact dates before final submission, make the review action unambiguous, and present the recurring private hub's plain-language status and valid confirm/withdraw choices before its chronological date list. |
| FR7 | Show an unobtrusive `Recover your private commitment link` route from public Open shifts to existing `/Commitments/Recover`, without changing its non-enumerating form or private-token lifecycle. |
| FR8 | Keep existing coordinator recurring Create, Publish, Revision, Handoff and schedule policy Preview/Save buttons connected to their named server handlers; reviewed server-projected version/mapping/consequence fields must survive the preview response rather than reusing empty posted values. No button may silently redisplay an unchanged form instead of taking its requested action. |
| FR9 | When editing a schedule entry, prefill the existing volunteer instructions and group-local start/end values so unchanged fields do not need to be re-entered; keep current correction and DST validation. |
| FR10 | For an eligible schedule policy change, the reviewed consequence text must be the same canonical consequence checked at authoritative save so confirmation does not fail on wording alone. |

## In scope

Existing Razor views/layout, `site.css`, Web presentation models, and the policy-consequence preview text in `src/VolunteerCoordinator.Application/VolunteerCoordinatorService.cs`. The existing #17/#22 projections and #18/#21 commands are consumed unchanged. Browser-check volunteer and coordinator routes at 320 CSS pixels plus keyboard disclosure and actions.

## Out of scope

Persistence/migrations, workflow state changes, new matching or bulk operations, new routes, auth/token expiration changes, notification policy, removal of previews or privacy text, and broad redesign of administrative forms.

## Versioning

The `enhancement` label requires one minor increment from root `VERSION` under technical steering and a matching `CHANGELOG.md` entry. Integration owner Main applies both once at exact-head delivery; this presentation slice does not independently bump or double-bump the version.
